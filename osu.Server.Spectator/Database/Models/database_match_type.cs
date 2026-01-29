// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.Rooms;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable once InconsistentNaming
    [Serializable]
    public enum database_match_type
    {
        playlists,
        head_to_head,
        team_versus,

        // Matchmaking: quick Play
        matchmaking,

        // Matchmaking: ranked play
        ranked_play
    }

    public static class DatabaseMatchTypeExtensions
    {
        public static MatchType ToMatchType(this database_match_type type)
        {
            switch (type)
            {
                case database_match_type.playlists:
                    return MatchType.Playlists;

                case database_match_type.head_to_head:
                    return MatchType.HeadToHead;

                case database_match_type.team_versus:
                    return MatchType.TeamVersus;

                case database_match_type.matchmaking:
                    return MatchType.Matchmaking;

                case database_match_type.ranked_play:
                    return MatchType.RankedPlay;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static database_match_type ToDatabaseMatchType(this MatchType type)
        {
            switch (type)
            {
                case MatchType.Playlists:
                    return database_match_type.playlists;

                case MatchType.HeadToHead:
                    return database_match_type.head_to_head;

                case MatchType.TeamVersus:
                    return database_match_type.team_versus;

                case MatchType.Matchmaking:
                    return database_match_type.matchmaking;

                case MatchType.RankedPlay:
                    return database_match_type.ranked_play;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
