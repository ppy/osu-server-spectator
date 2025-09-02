// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class matchmaking_user_stats
    {
        public uint user_id { get; set; }
        public ushort ruleset_id { get; set; }
        public uint first_placements { get; set; }
        public uint total_points { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset updated_at { get; set; }
    }
}
