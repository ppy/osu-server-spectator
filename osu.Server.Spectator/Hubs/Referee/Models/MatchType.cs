// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    /// <summary>
    /// Enumerates possible types of refereed matches.
    /// </summary>
    [PublicAPI]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MatchType
    {
        /// <summary>
        /// Players compete one against another.
        /// </summary>
        [JsonStringEnumMemberName("head_to_head")]
        HeadToHead = Game.Online.Rooms.MatchType.HeadToHead,

        /// <summary>
        /// Players are grouped in two teams which compete against each other.
        /// Users' total scores are tallied together to obtain a team total.
        /// </summary>
        [JsonStringEnumMemberName("team_versus")]
        TeamVersus = Game.Online.Rooms.MatchType.TeamVersus,
    }
}
