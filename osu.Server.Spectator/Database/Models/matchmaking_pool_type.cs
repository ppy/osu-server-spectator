// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public enum matchmaking_pool_type
    {
        quick_play,
        ranked_play
    }

    public static class MatchmakingPoolTypeExtensions
    {
        public static matchmaking_pool_type ToDatabasePoolType(this MatchmakingPoolType type)
        {
            switch (type)
            {
                case MatchmakingPoolType.QuickPlay:
                    return matchmaking_pool_type.quick_play;

                case MatchmakingPoolType.RankedPlay:
                    return matchmaking_pool_type.ranked_play;

                default:
                    throw new ArgumentException($"Unexpected pool type: {type}", nameof(type));
            }
        }

        public static MatchmakingPoolType ToPoolType(this matchmaking_pool_type type)
        {
            switch (type)
            {
                case matchmaking_pool_type.quick_play:
                    return MatchmakingPoolType.QuickPlay;

                case matchmaking_pool_type.ranked_play:
                    return MatchmakingPoolType.RankedPlay;

                default:
                    throw new ArgumentException($"Unexpected pool type: {type}", nameof(type));
            }
        }

        public static MatchType ToMatchType(this matchmaking_pool_type type)
        {
            switch (type)
            {
                case matchmaking_pool_type.quick_play:
                    return MatchType.Matchmaking;

                case matchmaking_pool_type.ranked_play:
                    return MatchType.RankedPlay;

                default:
                    throw new ArgumentException($"Unexpected pool type: {type}", nameof(type));
            }
        }
    }
}
