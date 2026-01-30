// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class matchmaking_pool_beatmap : IEquatable<matchmaking_pool_beatmap>
    {
        // matchmaking_pool_beatmaps
        public uint id { get; set; }
        public uint pool_id { get; set; }
        public int beatmap_id { get; set; }
        public string? mods { get; set; }
        public int? rating { get; set; }
        public int selection_count { get; set; }

        // osu_beatmaps
        public ushort playmode { get; set; }
        public string? checksum { get; set; }
        public double difficultyrating { get; set; }

        public MultiplayerPlaylistItem ToPlaylistItem() => new MultiplayerPlaylistItem
        {
            BeatmapID = beatmap_id,
            BeatmapChecksum = checksum!,
            RulesetID = playmode,
            StarRating = difficultyrating,
            RequiredMods = JsonConvert.DeserializeObject<APIMod[]>(mods ?? string.Empty) ?? [],
        };

        public bool Equals(matchmaking_pool_beatmap? other)
            => other != null
               && id == other.id
               && pool_id == other.pool_id
               && playmode == other.playmode
               && beatmap_id == other.beatmap_id
               && mods == other.mods;

        public override bool Equals(object? obj)
            => obj is matchmaking_pool_beatmap other && Equals(other);

        [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
        public override int GetHashCode()
            => HashCode.Combine(id, pool_id, beatmap_id, mods);
    }
}
