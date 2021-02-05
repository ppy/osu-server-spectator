// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Online.Multiplayer;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class multiplayer_playlist_item
    {
        public long id { get; set; }
        public long room_id { get; set; }
        public int beatmap_id { get; set; }
        public short ruleset_id { get; set; }
        public short? playlist_order { get; set; }
        public string? allowed_mods { get; set; }
        public string? required_mods { get; set; }
        public DateTimeOffset? created_at { get; set; }
        public DateTimeOffset? updated_at { get; set; }

        // for deserialization
        public multiplayer_playlist_item()
        {
        }

        /// <summary>
        /// Create a playlist item model from the latest settings in a room.
        /// </summary>
        /// <param name="room">The room to retrieve settings from.</param>
        public multiplayer_playlist_item(MultiplayerRoom room)
        {
            room_id = room.RoomID;

            beatmap_id = room.Settings.BeatmapID;
            ruleset_id = (short)room.Settings.RulesetID;
            required_mods = JsonConvert.SerializeObject(room.Settings.RequiredMods);
            allowed_mods = JsonConvert.SerializeObject(room.Settings.AllowedMods);
            updated_at = DateTimeOffset.Now;
        }
    }
}
