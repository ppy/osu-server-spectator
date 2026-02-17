// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Moves a user in the room.
    /// </summary>
    [PublicAPI]
    public class MoveUserRequest
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// The team to move the user to.
        /// </summary>
        [JsonPropertyName("team")]
        public MatchTeam Team { get; set; }
    }
}
