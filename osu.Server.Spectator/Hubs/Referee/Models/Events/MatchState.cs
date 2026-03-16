// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Framework.Extensions.TypeExtensions;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// Contains the current state of the room.
    /// </summary>
    [PublicAPI]
    [JsonDerivedType(typeof(TeamVersusMatchState))]
    public class MatchState
    {
        /// <summary>
        /// The current match type.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType Type { get; }

        protected MatchState(MatchType type)
        {
            Type = type;
        }

        internal static MatchState Create(MultiplayerRoom room)
        {
            switch (room.MatchState)
            {
                case null:
                    return new MatchState((MatchType)room.Settings.MatchType);

                case Game.Online.Multiplayer.MatchTypes.TeamVersus.TeamVersusRoomState teamVersusRoomState:
                    return new TeamVersusMatchState(teamVersusRoomState);

                default:
                    throw new ArgumentOutOfRangeException(nameof(room.MatchState), room.MatchState, $"Cannot handle room state of type {room.MatchState.GetType().ReadableName()}");
            }
        }
    }
}
