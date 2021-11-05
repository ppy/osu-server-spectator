// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.Rooms;

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
        public bool expired { get; set; }

        // for deserialization
        public multiplayer_playlist_item()
        {
        }

        /// <summary>
        /// Creates a playlist item model from an <see cref="APIPlaylistItem"/> for the given room ID.
        /// </summary>
        /// <param name="roomId">The room ID to create the playlist item model for.</param>
        /// <param name="item">The <see cref="APIPlaylistItem"/> to retrieve data from.</param>
        public multiplayer_playlist_item(long roomId, APIPlaylistItem item)
        {
            id = item.ID;
            room_id = roomId;
            beatmap_id = item.BeatmapID;
            ruleset_id = (short)item.RulesetID;
            required_mods = JsonConvert.SerializeObject(item.RequiredMods);
            allowed_mods = JsonConvert.SerializeObject(item.AllowedMods);
            updated_at = DateTimeOffset.Now;
            expired = item.Expired;
        }

        public async Task<APIPlaylistItem> ToAPIPlaylistItem(IDatabaseAccess db) => new APIPlaylistItem
        {
            ID = id,
            BeatmapID = beatmap_id,
            BeatmapChecksum = (await db.GetBeatmapChecksumAsync(beatmap_id)) ?? string.Empty,
            RulesetID = ruleset_id,
            RequiredMods = JsonConvert.DeserializeObject<APIMod[]>(required_mods ?? string.Empty) ?? Array.Empty<APIMod>(),
            AllowedMods = JsonConvert.DeserializeObject<APIMod[]>(allowed_mods ?? string.Empty) ?? Array.Empty<APIMod>(),
            Expired = expired
        };
    }
}
