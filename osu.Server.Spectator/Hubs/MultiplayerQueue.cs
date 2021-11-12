// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Queueing;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerQueue
    {
        private readonly ServerMultiplayerRoom room;
        private readonly IDatabaseFactory dbFactory;
        private readonly IMultiplayerServerMatchCallbacks hub;

        private APIPlaylistItem? currentItem;
        private QueueModes mode;

        public MultiplayerQueue(ServerMultiplayerRoom room, IDatabaseFactory dbFactory, IMultiplayerServerMatchCallbacks hub)
        {
            this.room = room;
            this.dbFactory = dbFactory;
            this.hub = hub;
        }

        /// <summary>
        /// The current <see cref="APIPlaylistItem"/>
        /// </summary>
        public APIPlaylistItem CurrentItem
        {
            get
            {
                if (currentItem == null)
                    throw new InvalidOperationException("Room not initialised.");

                return currentItem;
            }
            private set => currentItem = value;
        }

        /// <summary>
        /// Initialises the queue from the database.
        /// </summary>
        public async Task Initialise()
        {
            mode = room.Settings.QueueMode;
            await updateCurrentItem(false);
        }

        /// <summary>
        /// Changes the queueing mode.
        /// </summary>
        /// <param name="newMode">The new mode.</param>
        public async Task ChangeMode(QueueModes newMode)
        {
            if (mode == newMode)
                return;

            if (newMode == QueueModes.HostOnly)
            {
                // When changing to host-only mode, ensure that exactly one non-expired playlist item exists.
                using (var db = dbFactory.GetInstance())
                {
                    // Remove all but the current and expired items. The current item may be re-used for host-only mode if it's non-expired.
                    foreach (var item in await db.GetAllPlaylistItems(room.RoomID))
                    {
                        if (item.expired || item.id == room.Settings.PlaylistItemId)
                            continue;

                        await db.RemovePlaylistItemAsync(room.RoomID, item.id);
                        await hub.OnPlaylistItemRemoved(room, item.id);
                    }

                    // Always ensure that at least one non-expired item exists by duplicating the current item if required.
                    if (CurrentItem.Expired)
                        await duplicateCurrentItem(db);
                }
            }

            mode = newMode;

            // When changing modes, items could have been added (above) or the queueing order could have change.
            await updateCurrentItem();
        }

        /// <summary>
        /// Expires the current playlist item and advances to the next one in the order defined by the queueing mode.
        /// </summary>
        public async Task FinishCurrentItem()
        {
            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.ExpirePlaylistItemAsync(CurrentItem.ID);
                CurrentItem.Expired = true;
                await hub.OnPlaylistItemChanged(room, CurrentItem);

                // In host-only mode, duplicate the playlist item for the next round.
                if (mode == QueueModes.HostOnly)
                    await duplicateCurrentItem(db);
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Add a playlist item to the room's queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="user">The user adding the item.</param>
        /// <exception cref="NotHostException">If the adding user is not the host in host-only mode.</exception>
        /// <exception cref="InvalidStateException">If the given playlist item is not valid.</exception>
        public async Task AddItem(APIPlaylistItem item, MultiplayerRoomUser user)
        {
            if (mode == QueueModes.HostOnly && (room.Host == null || !user.Equals(room.Host)))
                throw new NotHostException();

            using (var db = dbFactory.GetInstance())
            {
                string? beatmapChecksum = await db.GetBeatmapChecksumAsync(item.BeatmapID);

                if (beatmapChecksum == null)
                    throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");

                if (item.BeatmapChecksum != beatmapChecksum)
                    throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

                if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                    throw new InvalidStateException("Attempted to select an unsupported ruleset.");

                ensureModsValid(item);

                switch (mode)
                {
                    case QueueModes.HostOnly:
                        // In host-only mode, the current item is re-used.
                        item.ID = CurrentItem.ID;
                        item.UserID = CurrentItem.UserID;

                        // Although the playlist item ID doesn't change, its contents may.
                        // We need to update the stored current item for the OnPlaylistItemChanged() callback to correctly validate user mods.
                        CurrentItem = item;

                        await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        await hub.OnPlaylistItemChanged(room, item);
                        break;

                    default:
                        item.UserID = user.UserID;
                        item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        await hub.OnPlaylistItemAdded(room, item);

                        // The current item can change as a result of an item being added. For example, if all items earlier in the queue were expired.
                        await updateCurrentItem();
                        break;
                }
            }
        }

        public Task RemoveItem(long playlistItemId, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            throw new InvalidStateException("Items cannot yet be removed from the playlist.");
        }

        /// <summary>
        /// Checks whether the given mods are compatible with the current playlist item's mods and ruleset.
        /// </summary>
        /// <param name="proposedMods">The mods to check against the current playlist item.</param>
        /// <param name="validMods">The set of mods which _are_ valid.</param>
        /// <returns>Whether all mods are valid for the current playlist item.</returns>
        public bool ValidateMods(IEnumerable<APIMod> proposedMods, [NotNullWhen(false)] out IEnumerable<APIMod>? validMods)
        {
            bool proposedWereValid = true;
            proposedWereValid &= populateValidModsForRuleset(CurrentItem.RulesetID, proposedMods, out var valid);

            // check allowed by room
            foreach (var mod in valid.ToList())
            {
                if (CurrentItem.AllowedMods.All(m => m.Acronym != mod.Acronym))
                {
                    valid.Remove(mod);
                    proposedWereValid = false;
                }
            }

            // check valid as combination
            if (!ModUtils.CheckCompatibleSet(valid, out var invalid))
            {
                proposedWereValid = false;
                foreach (var mod in invalid)
                    valid.Remove(mod);
            }

            validMods = valid.Select(m => new APIMod(m));

            return proposedWereValid;
        }

        /// <summary>
        /// Ensures that a <see cref="APIPlaylistItem"/>'s required and allowed mods are compatible with each other and the room's ruleset.
        /// </summary>
        /// <param name="item">The <see cref="APIPlaylistItem"/> to validate.</param>
        /// <exception cref="InvalidStateException">If the mods are invalid.</exception>
        private static void ensureModsValid(APIPlaylistItem item)
        {
            // check against ruleset
            if (!populateValidModsForRuleset(item.RulesetID, item.RequiredMods, out var requiredMods))
            {
                var invalidRequiredAcronyms = string.Join(',', item.RequiredMods.Where(m => requiredMods.All(valid => valid.Acronym != m.Acronym)).Select(m => m.Acronym));
                throw new InvalidStateException($"Invalid mods were selected for specified ruleset: {invalidRequiredAcronyms}");
            }

            if (!populateValidModsForRuleset(item.RulesetID, item.AllowedMods, out var allowedMods))
            {
                var invalidAllowedAcronyms = string.Join(',', item.AllowedMods.Where(m => allowedMods.All(valid => valid.Acronym != m.Acronym)).Select(m => m.Acronym));
                throw new InvalidStateException($"Invalid mods were selected for specified ruleset: {invalidAllowedAcronyms}");
            }

            if (!ModUtils.CheckCompatibleSet(requiredMods, out var invalid))
                throw new InvalidStateException($"Invalid combination of required mods: {string.Join(',', invalid.Select(m => m.Acronym))}");

            // check aggregate combinations with each allowed mod individually.
            foreach (var allowedMod in allowedMods)
            {
                if (!ModUtils.CheckCompatibleSet(requiredMods.Concat(new[] { allowedMod }), out invalid))
                    throw new InvalidStateException($"Invalid combination of required and allowed mods: {string.Join(',', invalid.Select(m => m.Acronym))}");
            }
        }

        /// <summary>
        /// Verifies all proposed mods are valid for the room's ruleset, returning instantiated <see cref="Mod"/>s for further processing.
        /// </summary>
        /// <param name="rulesetID">The legacy ruleset ID to check against.</param>
        /// <param name="proposedMods">The proposed mods.</param>
        /// <param name="valid">A list of valid deserialised mods.</param>
        /// <returns>Whether all <see cref="proposedMods"/> were valid.</returns>
        private static bool populateValidModsForRuleset(int rulesetID, IEnumerable<APIMod> proposedMods, out List<Mod> valid)
        {
            valid = new List<Mod>();
            bool proposedWereValid = true;

            var ruleset = LegacyHelper.GetRulesetFromLegacyID(rulesetID);

            foreach (var apiMod in proposedMods)
            {
                try
                {
                    // will throw if invalid
                    valid.Add(apiMod.ToMod(ruleset));
                }
                catch
                {
                    proposedWereValid = false;
                }
            }

            return proposedWereValid;
        }

        /// <summary>
        /// Duplicates <see cref="CurrentItem"/> into the database.
        /// </summary>
        /// <param name="db">The database connection.</param>
        private async Task duplicateCurrentItem(IDatabaseAccess db)
        {
            var newItem = new APIPlaylistItem
            {
                UserID = CurrentItem.UserID,
                BeatmapID = CurrentItem.BeatmapID,
                BeatmapChecksum = CurrentItem.BeatmapChecksum,
                RulesetID = CurrentItem.RulesetID,
                AllowedMods = CurrentItem.AllowedMods,
                RequiredMods = CurrentItem.RequiredMods
            };

            newItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, newItem));
            await hub.OnPlaylistItemAdded(room, newItem);
        }

        /// <summary>
        /// Updates <see cref="CurrentItem"/> and the playlist item ID stored in the room's settings.
        /// </summary>
        /// <param name="notifyHub">Whether to notify the <see cref="MultiplayerHub"/> of the change.</param>
        private async Task updateCurrentItem(bool notifyHub = true)
        {
            using (var db = dbFactory.GetInstance())
            {
                var allItems = await db.GetAllPlaylistItems(room.RoomID);

                switch (mode)
                {
                    default:
                        // Pick the first available non-expired playlist item, or default to the last item for when all items are expired.
                        CurrentItem = await (allItems.FirstOrDefault(i => !i.expired) ?? allItems.Last()).ToAPIPlaylistItem(db);
                        break;

                    case QueueModes.FairRotate:
                        CurrentItem = await (allItems
                                             // Group items by user_id.
                                             .GroupBy(i => i.user_id)
                                             // Order users by descending number of expired (already played) items.
                                             .OrderBy(g => g.Count(i => i.expired))
                                             // Get the first unexpired item from each group.
                                             .Select(g => g.FirstOrDefault(i => !i.expired))
                                             // Select the first unexpired item in order.
                                             .FirstOrDefault(i => i != null)
                                             // Default to the last item for when all items are expired.
                                             ?? allItems.Last())
                            .ToAPIPlaylistItem(db);
                        break;
                }
            }

            long lastItem = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = CurrentItem.ID;

            if (notifyHub && CurrentItem.ID != lastItem)
                await hub.OnMatchSettingsChanged(room);
        }
    }
}
