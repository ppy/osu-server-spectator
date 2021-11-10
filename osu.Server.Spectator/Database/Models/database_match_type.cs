// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.Rooms;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public enum database_match_type
    {
        playlists,
        head_to_head,
        team_versus,
    }

    public static class DatabaseMatchTypeExtensions
    {
        public static MatchType ToMatchType(this database_match_type type)
        {
            switch (type)
            {
                case database_match_type.playlists:
                    return MatchType.Playlists;

                default:
                case database_match_type.head_to_head:
                    return MatchType.HeadToHead;

                case database_match_type.team_versus:
                    return MatchType.TeamVersus;
            }
        }

        public static database_match_type ToDatabaseMatchType(this MatchType type)
        {
            switch (type)
            {
                case MatchType.Playlists:
                    return database_match_type.playlists;

                default:
                case MatchType.HeadToHead:
                    return database_match_type.head_to_head;

                case MatchType.TeamVersus:
                    return database_match_type.team_versus;
            }
        }
    }
}
