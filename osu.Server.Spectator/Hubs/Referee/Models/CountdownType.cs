// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    /// <summary>
    /// Enumerates types of countdowns relevant to referees.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CountdownType
    {
        /// <summary>
        /// A match will start in the room once this countdown completes.
        /// </summary>
        [JsonStringEnumMemberName("match_start")]
        MatchStart,

        /// <summary>
        /// This instance of the spectator server will shut down once this countdown completes.
        /// </summary>
        [JsonStringEnumMemberName("server_shutting_down")]
        ServerShuttingDown,
    }
}
