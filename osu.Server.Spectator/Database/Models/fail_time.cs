// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class fail_time
    {
        public int beatmap_id { get; set; }
        public required byte[] exit { get; set; }
        public required byte[] fail { get; set; }
    }
}