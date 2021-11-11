// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        private readonly IMultiplayerServerMatchCallbacks hub;

        private QueueModes mode;

        public MultiplayerQueue(ServerMultiplayerRoom room, IMultiplayerServerMatchCallbacks hub)
        {
            this.room = room;
            this.hub = hub;
        }

        public async Task<multiplayer_playlist_item> GetCurrentItem(IDatabaseAccess db)
        {
            var allItems = await db.GetAllPlaylistItems(room.RoomID);

            switch (mode)
            {
                default:
                    // Pick the first available non-expired playlist item, or default to the last item for when all items are expired.
                    return allItems.FirstOrDefault(i => !i.expired) ?? allItems.Last();

                case QueueModes.FairRotate:
                    return allItems
                           // Group items by user_id.
                           .GroupBy(i => i.user_id)
                           // Order users by descending number of expired (already played) items.
                           .OrderBy(g => g.Count(i => i.expired))
                           // Get the first unexpired item from each group.
                           .Select(g => g.FirstOrDefault(i => !i.expired))
                           // Select the first unexpired item in order.
                           .FirstOrDefault(i => i != null)
                           // Default to the last item for when all items are expired.
                           ?? allItems.Last();
            }
        }

        public async Task ChangeMode(QueueModes newMode, IDatabaseAccess db)
        {
            if (mode == newMode)
                return;

            if (newMode == QueueModes.HostOnly)
            {
                // Remove all but the current and expired items. The current item will be used for the host-only queue.
                foreach (var item in await db.GetAllPlaylistItems(room.RoomID))
                {
                    if (item.expired || item.id == room.Settings.PlaylistItemId)
                        continue;

                    await removeItem(item.id, db);
                }
            }

            mode = newMode;
        }

        public async Task FinishCurrentItem(IDatabaseAccess db)
        {
            // Expire the current playlist item.
            var item = await GetCurrentItem(db);
            await db.ExpirePlaylistItemAsync(item.id);

            // Re-retrieve the updated item to notify clients with.
            item = (await db.GetPlaylistItemFromRoomAsync(room.RoomID, item.id))!;
            await hub.OnPlaylistItemChanged(room, await item.ToAPIPlaylistItem(db));

            if (mode != QueueModes.HostOnly)
                return;

            // In host-only mode, duplicate the playlist item for the next round.
            item.id = await db.AddPlaylistItemAsync(item);

            // Re-retrieve the updated item to notify clients with.
            item = (await db.GetPlaylistItemFromRoomAsync(room.RoomID, item.id))!;
            await hub.OnPlaylistItemAdded(room, await item.ToAPIPlaylistItem(db));
        }

        public async Task AddItem(APIPlaylistItem item, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            if (mode == QueueModes.HostOnly && (room.Host == null || !user.Equals(room.Host)))
                throw new NotHostException();

            string? beatmapChecksum = await db.GetBeatmapChecksumAsync(item.BeatmapID);
            if (beatmapChecksum == null)
                throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");
            if (item.BeatmapChecksum != beatmapChecksum)
                throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

            if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                throw new InvalidStateException("Attempted to select an unsupported ruleset.");

            ensureModsValid(item);

            bool hasItems = await db.HasPlaylistItems(room.RoomID);

            switch (mode)
            {
                case QueueModes.HostOnly when hasItems: // In host-only mode, re-use the current item if able to.
                    item.ID = (await GetCurrentItem(db)).id;
                    await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                    await hub.OnPlaylistItemChanged(room, item);
                    break;

                default:
                    item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item) { user_id = user.UserID });
                    await hub.OnPlaylistItemAdded(room, item);
                    break;
            }
        }

        public Task RemoveItem(long playlistItemId, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            throw new InvalidStateException("Items cannot yet be removed from the playlist.");
        }

        private async Task removeItem(long playlistItemId, IDatabaseAccess db)
        {
            await db.RemovePlaylistItemAsync(room.RoomID, playlistItemId);
            await hub.OnPlaylistItemRemoved(room, playlistItemId);
        }

        public async Task<(bool isValid, IEnumerable<APIMod> validMods)> ValidateMods(IDatabaseAccess db, IEnumerable<APIMod> proposedMods)
        {
            multiplayer_playlist_item currentItem = await GetCurrentItem(db);
            APIMod[] allowedMods = JsonConvert.DeserializeObject<APIMod[]>(currentItem.allowed_mods ?? string.Empty) ?? Array.Empty<APIMod>();

            bool proposedWereValid = true;
            proposedWereValid &= populateValidModsForRuleset(currentItem.ruleset_id, proposedMods, out var valid);

            // check allowed by room
            foreach (var mod in valid.ToList())
            {
                if (allowedMods.All(m => m.Acronym != mod.Acronym))
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

            return (proposedWereValid, valid.Select(m => new APIMod(m)).ToArray());
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
    }
}
