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
        public string? checksum { get; set; }
        public BeatmapOnlineStatus approved { get; set; }
        public double difficultyrating { get; set; }
    }
}
