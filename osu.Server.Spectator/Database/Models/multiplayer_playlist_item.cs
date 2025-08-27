// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
        public int owner_id { get; set; }
        public long room_id { get; set; }
        public int beatmap_id { get; set; }
        public short ruleset_id { get; set; }
        public ushort? playlist_order { get; set; }
        public string? allowed_mods { get; set; }
        public string? required_mods { get; set; }
        public bool freestyle { get; set; }

        /// <summary>
        /// Changes to this property will not be persisted to the database.
        /// </summary>
        public DateTimeOffset? created_at { get; set; }

        /// <summary>
        /// Changes to this property will not be persisted to the database.
        /// </summary>
        public DateTimeOffset? updated_at { get; set; }

        public bool expired { get; set; }

        /// <summary>
        /// Changes to this property will not be persisted to the database.
        /// </summary>
        public DateTimeOffset? played_at { get; set; }

        /// <summary>
        /// The beatmap checksum.
        /// </summary>
        public string? checksum { get; set; }

        /// <summary>
        /// The beatmap difficulty rating.
        /// </summary>
        public double difficultyrating { get; set; }

        // for deserialization
        public multiplayer_playlist_item()
        {
        }

        /// <summary>
        /// Creates a playlist item model from an <see cref="MultiplayerPlaylistItem"/> for the given room ID.
        /// </summary>
        /// <param name="roomId">The room ID to create the playlist item model for.</param>
        /// <param name="item">The <see cref="MultiplayerPlaylistItem"/> to retrieve data from.</param>
        public multiplayer_playlist_item(long roomId, MultiplayerPlaylistItem item)
        {
            id = item.ID;
            owner_id = item.OwnerID;
            room_id = roomId;
            beatmap_id = item.BeatmapID;
            ruleset_id = (short)item.RulesetID;
            required_mods = JsonConvert.SerializeObject(item.RequiredMods);
            allowed_mods = JsonConvert.SerializeObject(item.AllowedMods);
            freestyle = item.Freestyle;
            updated_at = DateTimeOffset.Now;
            expired = item.Expired;
            playlist_order = item.PlaylistOrder;
            played_at = item.PlayedAt;
        }

        public MultiplayerPlaylistItem ToMultiplayerPlaylistItem()
        {
            var playlistItem = new MultiplayerPlaylistItem
            {
                ID = id,
                OwnerID = owner_id,
                BeatmapID = beatmap_id,
                BeatmapChecksum = checksum ?? string.Empty,
                RulesetID = ruleset_id,
                RequiredMods = JsonConvert.DeserializeObject<APIMod[]>(required_mods ?? string.Empty) ?? Array.Empty<APIMod>(),
                AllowedMods = JsonConvert.DeserializeObject<APIMod[]>(allowed_mods ?? string.Empty) ?? Array.Empty<APIMod>(),
                Freestyle = freestyle,
                Expired = expired,
                PlaylistOrder = playlist_order ?? 0,
                PlayedAt = played_at,
                StarRating = difficultyrating
            };
            return playlistItem;
        }

        public multiplayer_playlist_item Clone() => (multiplayer_playlist_item)MemberwiseClone();
    }
}
