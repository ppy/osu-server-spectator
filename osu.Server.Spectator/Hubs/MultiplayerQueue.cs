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
        public QueueModes Mode { get; set; }

        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerServerMatchCallbacks hub;

        public MultiplayerQueue(ServerMultiplayerRoom room, IMultiplayerServerMatchCallbacks hub)
        {
            this.room = room;
            this.hub = hub;
        }

        public async Task<multiplayer_playlist_item> GetCurrentItem(IDatabaseAccess db)
        {
            switch (Mode)
            {
                default:
                    return await db.GetCandidatePlaylistItemByExpiry(room.RoomID);

                case QueueModes.FairRotate:
                    return await db.GetCandidatePlaylistItemByFairness(room.RoomID);
            }
        }

        public async Task FinishCurrentItem(IDatabaseAccess db)
        {
            var currentItem = await GetCurrentItem(db);

            // Expire the current playlist item.
            await db.ExpirePlaylistItemAsync(currentItem.id);
            await hub.OnPlaylistItemChanged(room, await currentItem.ToAPIPlaylistItem(db));

            // In host-only mode, a duplicate playlist item will be used for the next round.
            if (Mode == QueueModes.HostOnly)
                await db.AddPlaylistItemAsync(currentItem);
        }

        public async Task AddItem(APIPlaylistItem item, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            if (Mode == QueueModes.HostOnly && (room.Host == null || !user.Equals(room.Host)))
                throw new NotHostException();

            ensureModsValid(item);

            string? beatmapChecksum = await db.GetBeatmapChecksumAsync(item.BeatmapID);
            if (beatmapChecksum == null)
                throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");
            if (item.BeatmapChecksum != beatmapChecksum)
                throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

            if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                throw new InvalidStateException("Attempted to select an unsupported ruleset.");

            bool hasItems = await db.HasPlaylistItems(room.RoomID);

            switch (Mode)
            {
                case QueueModes.HostOnly when hasItems: // In host-only mode, re-use the current item if able to.
                    item.ID = (await GetCurrentItem(db)).id;
                    await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                    await hub.OnPlaylistItemChanged(room, item);
                    break;

                default:
                    item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                    await hub.OnPlaylistItemAdded(room, item);
                    break;
            }
        }

        public async Task RemoveItem(APIPlaylistItem item, MultiplayerRoomUser user, IDatabaseAccess db)
        {
            if (room.Host == null || !user.Equals(room.Host))
                throw new NotHostException();

            await db.RemovePlaylistItemAsync(room.RoomID, item.ID);
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
