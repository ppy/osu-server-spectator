// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// Room state specific to <see cref="MatchType.TeamVersus"/>.
    /// </summary>
    [PublicAPI]
    public class TeamVersusMatchState : MatchState
    {
        /// <summary>
        /// Whether the room is currently locked.
        /// When the room is locked, players cannot change teams themselves, only referees can change it for them.
        /// </summary>
        [JsonPropertyName("locked")]
        public bool Locked { get; }

        public TeamVersusMatchState(Game.Online.Multiplayer.MatchTypes.TeamVersus.TeamVersusRoomState state)
            : base(MatchType.TeamVersus)
        {
            Locked = state.Locked;
        }
    }
}
