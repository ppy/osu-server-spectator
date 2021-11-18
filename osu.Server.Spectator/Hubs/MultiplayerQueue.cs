// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Queueing;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Utils;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerQueue
    {
        public const int PER_USER_LIMIT = 3;

        private readonly ServerMultiplayerRoom room;
        private readonly IDatabaseFactory dbFactory;
        private readonly IMultiplayerServerMatchCallbacks hub;

        private int currentIndex;

        public MultiplayerQueue(ServerMultiplayerRoom room, IDatabaseFactory dbFactory, IMultiplayerServerMatchCallbacks hub)
        {
            this.room = room;
            this.dbFactory = dbFactory;
            this.hub = hub;
        }

        public MultiplayerPlaylistItem CurrentItem => room.Playlist[currentIndex];

        /// <summary>
        /// Initialises the queue from the database.
        /// </summary>
        public async Task Initialise()
        {
            using (var db = dbFactory.GetInstance())
            {
                foreach (var item in await db.GetAllPlaylistItemsAsync(room.RoomID))
                    room.Playlist.Add(await item.ToMultiplayerPlaylistItem(db));
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Updates the queue as a result of a change in the queueing mode.
        /// </summary>
        public async Task UpdateFromQueueModeChange()
        {
            if (room.Settings.QueueMode == QueueMode.HostOnly && room.Playlist.All(item => item.Expired))
            {
                // When changing to host-only mode, ensure that exactly one non-expired playlist item exists by duplicating the currrent item.
                using (var db = dbFactory.GetInstance())
                    await duplicateCurrentItem(db);
            }

            // When changing modes, items could have been added (above) or the queueing order could have changed.
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

                // In host-only mode, duplicate the playlist item for the next round if no other non-expired items exist.
                if (room.Settings.QueueMode == QueueMode.HostOnly)
                {
                    if (room.Playlist.All(item => item.Expired))
                        await duplicateCurrentItem(db);
                }
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
        public async Task AddItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            if (room.Settings.QueueMode == QueueMode.HostOnly && !user.Equals(room.Host))
                throw new NotHostException();

            if (room.Playlist.Count(i => i.OwnerID == user.UserID && !i.Expired) >= PER_USER_LIMIT)
                throw new InvalidStateException($"Can't enqueue more than {PER_USER_LIMIT} items at once.");

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

                switch (room.Settings.QueueMode)
                {
                    case QueueMode.HostOnly:
                        // In host-only mode, the current item is re-used.
                        item.ID = CurrentItem.ID;
                        item.OwnerID = CurrentItem.OwnerID;

                        await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        room.Playlist[currentIndex] = item;

                        await hub.OnPlaylistItemChanged(room, item);
                        break;

                    default:
                        item.OwnerID = user.UserID;
                        item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        room.Playlist.Add(item);

                        await hub.OnPlaylistItemAdded(room, item);

                        // The current item can change as a result of an item being added. For example, if all items earlier in the queue were expired.
                        await updateCurrentItem();
                        break;
                }
            }
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
            proposedWereValid &= ModUtils.InstantiateValidModsForRuleset(LegacyHelper.GetRulesetFromLegacyID(CurrentItem.RulesetID), proposedMods, out var valid);

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
        /// Ensures that a <see cref="MultiplayerPlaylistItem"/>'s required and allowed mods are compatible with each other and the room's ruleset.
        /// </summary>
        /// <param name="item">The <see cref="MultiplayerPlaylistItem"/> to validate.</param>
        /// <exception cref="InvalidStateException">If the mods are invalid.</exception>
        private static void ensureModsValid(MultiplayerPlaylistItem item)
        {
            var ruleset = LegacyHelper.GetRulesetFromLegacyID(item.RulesetID);

            // check against ruleset
            if (!ModUtils.InstantiateValidModsForRuleset(ruleset, item.RequiredMods, out var requiredMods))
            {
                var invalidRequiredAcronyms = string.Join(',', item.RequiredMods.Where(m => requiredMods.All(valid => valid.Acronym != m.Acronym)).Select(m => m.Acronym));
                throw new InvalidStateException($"Invalid mods were selected for specified ruleset: {invalidRequiredAcronyms}");
            }

            if (!ModUtils.InstantiateValidModsForRuleset(ruleset, item.AllowedMods, out var allowedMods))
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
        /// Duplicates <see cref="CurrentItem"/> into the database.
        /// </summary>
        /// <param name="db">The database connection.</param>
        private async Task duplicateCurrentItem(IDatabaseAccess db)
        {
            var newItem = new MultiplayerPlaylistItem
            {
                OwnerID = CurrentItem.OwnerID,
                BeatmapID = CurrentItem.BeatmapID,
                BeatmapChecksum = CurrentItem.BeatmapChecksum,
                RulesetID = CurrentItem.RulesetID,
                AllowedMods = CurrentItem.AllowedMods,
                RequiredMods = CurrentItem.RequiredMods
            };

            newItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, newItem));
            room.Playlist.Add(newItem);

            await hub.OnPlaylistItemAdded(room, newItem);
        }

        /// <summary>
        /// Updates <see cref="CurrentItem"/> and the playlist item ID stored in the room's settings.
        /// </summary>
        private async Task updateCurrentItem()
        {
            MultiplayerPlaylistItem newItem;

            switch (room.Settings.QueueMode)
            {
                default:
                    // Pick the first available non-expired playlist item, or default to the last item for when all items are expired.
                    newItem = room.Playlist.FirstOrDefault(i => !i.Expired) ?? room.Playlist.Last();
                    break;

                case QueueMode.FairRotate:
                    newItem =
                        room.Playlist
                            // Group items by their owner.
                            .GroupBy(i => i.OwnerID)
                            // Order by descending number of expired (already played) items for each owner.
                            .OrderBy(g => g.Count(i => i.Expired))
                            // Get the first unexpired item from each owner.
                            .Select(g => g.FirstOrDefault(i => !i.Expired))
                            // Select the first unexpired item in order.
                            .FirstOrDefault(i => i != null)
                        // Default to the last item for when all items are expired.
                        ?? room.Playlist.Last();
                    break;
            }

            currentIndex = room.Playlist.IndexOf(newItem);

            long lastItemID = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = newItem.ID;

            if (newItem.ID != lastItemID)
                await hub.OnMatchSettingsChanged(room);
        }
    }
}
