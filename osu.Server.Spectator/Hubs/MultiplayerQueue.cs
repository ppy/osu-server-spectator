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

        public APIPlaylistItem CurrentItem { get; private set; } = null!;

        private QueueModes mode;

        public MultiplayerQueue(ServerMultiplayerRoom room, IDatabaseFactory dbFactory, IMultiplayerServerMatchCallbacks hub)
        {
            this.room = room;
            this.dbFactory = dbFactory;
            this.hub = hub;
        }

        public async Task Initialise()
        {
            await refreshCurrentItem(false);
        }

        public async Task ChangeMode(QueueModes newMode)
        {
            if (mode == newMode)
                return;

            mode = newMode;

            if (newMode != QueueModes.HostOnly)
                return;

            using (var db = dbFactory.GetInstance())
            {
                // Remove all but the current and expired items. The current item will be used for the host-only queue.
                foreach (var item in await db.GetAllPlaylistItems(room.RoomID))
                {
                    if (item.expired || item.id == room.Settings.PlaylistItemId)
                        continue;

                    await db.RemovePlaylistItemAsync(room.RoomID, item.id);
                    await hub.OnPlaylistItemRemoved(room, item.id);
                }
            }
        }

        public async Task FinishCurrentItem()
        {
            using (var db = dbFactory.GetInstance())
            {
                await db.ExpirePlaylistItemAsync(CurrentItem.ID);

                CurrentItem.Expired = true;
                await hub.OnPlaylistItemChanged(room, CurrentItem);

                if (mode == QueueModes.HostOnly)
                {
                    // In host-only mode, duplicate the playlist item for the next round.
                    var newItem = new APIPlaylistItem
                    {
                        BeatmapID = CurrentItem.BeatmapID,
                        BeatmapChecksum = CurrentItem.BeatmapChecksum,
                        RulesetID = CurrentItem.RulesetID,
                        AllowedMods = CurrentItem.AllowedMods,
                        RequiredMods = CurrentItem.RequiredMods
                    };

                    newItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, newItem));
                    await hub.OnPlaylistItemAdded(room, newItem);
                }
            }

            await refreshCurrentItem();
        }

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
                    case QueueModes.HostOnly: // In host-only mode, re-use the current item if able to.
                        item.ID = CurrentItem.ID;
                        CurrentItem = item;
                        await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        await hub.OnPlaylistItemChanged(room, item);
                        break;

                    default:
                        item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        await hub.OnPlaylistItemAdded(room, item);
                        await refreshCurrentItem();
                        break;
                }
            }
        }

        public Task RemoveItem(long playlistItemId, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            throw new InvalidStateException("Items cannot yet be removed from the playlist.");
        }

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

        private async Task refreshCurrentItem(bool notifyHub = true)
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
                        // Todo: Group playlist items by (user_id -> count_expired), and select the first available playlist item from a user that has available beatmaps where count_expired is the lowest.
                        throw new NotImplementedException();
                }
            }

            long lastItem = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = CurrentItem.ID;

            if (notifyHub && CurrentItem.ID != lastItem)
                await hub.OnMatchSettingsChanged(room);
        }
    }
}
