// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;
using osu.Game.Beatmaps;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class database_beatmap
    {
        public int beatmap_id { get; set; }
        public int beatmapset_id { get; set; }
        public string? checksum { get; set; }
        public BeatmapOnlineStatus approved { get; set; }
        public double difficultyrating { get; set; }
        public ushort playmode { get; set; }
        public ushort osu_file_version { get; set; } = 14;
    }
}
