// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    /// <summary>
    /// Enumerates user statuses exposed to referees.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MatchUserStatus
    {
        /// <summary>
        /// The player is idle in the lobby.
        /// </summary>
        [JsonStringEnumMemberName("idle")]
        Idle,

        /// <summary>
        /// The player is in the lobby, readied up for gameplay.
        /// </summary>
        [JsonStringEnumMemberName("ready")]
        Ready,

        /// <summary>
        /// The user is in gameplay.
        /// </summary>
        [JsonStringEnumMemberName("playing")]
        Playing,

        /// <summary>
        /// The user has finished gameplay.
        /// </summary>
        [JsonStringEnumMemberName("finished_play")]
        FinishedPlay,

        /// <summary>
        /// The user is either actively spectating gameplay or waiting for gameplay to start in order to spectate it.
        /// </summary>
        [JsonStringEnumMemberName("spectating")]
        Spectating
    }

    public static class MatchUserStatusExtensions
    {
        /// <summary>
        /// Maps a <see cref="MultiplayerUserState"/> to a <see cref="MatchUserStatus"/>.
        /// </summary>
        /// <remarks>
        /// Not all <see cref="MultiplayerUserState"/>s are desirable for exposition to referee clients.
        /// </remarks>
        public static MatchUserStatus? ToMatchUserStatus(this MultiplayerUserState state)
        {
            switch (state)
            {
                case MultiplayerUserState.Idle:
                    return MatchUserStatus.Idle;

                case MultiplayerUserState.Ready:
                    return MatchUserStatus.Ready;

                case MultiplayerUserState.Playing:
                    return MatchUserStatus.Playing;

                case MultiplayerUserState.FinishedPlay:
                    return MatchUserStatus.FinishedPlay;

                case MultiplayerUserState.Spectating:
                    return MatchUserStatus.Spectating;
            }

            return null;
        }
    }
}
