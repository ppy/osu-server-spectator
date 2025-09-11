// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;
using osu.Game.Online.Matchmaking;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class matchmaking_pool
    {
        public int id { get; set; }
        public int ruleset_id { get; set; }
        public int variant_id { get; set; }
        public string name { get; set; } = string.Empty;
        public bool active { get; set; }

        public MatchmakingPool ToMatchmakingPool() => new MatchmakingPool
        {
            Id = id,
            RulesetId = ruleset_id,
            Variant = variant_id,
            Name = name,
        };
    }
}
