// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class matchmaking_pool_beatmap
    {
        // matchmaking_pool_beatmaps
        public int pool_id { get; set; }
        public int beatmap_id { get; set; }
        public string mods { get; set; } = string.Empty;

        // osu_beatmaps
        public string? checksum { get; set; }
        public double difficultyrating { get; set; }
    }
}
