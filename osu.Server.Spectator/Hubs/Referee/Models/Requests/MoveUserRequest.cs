// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Moves a user in the room.
    /// </summary>
    public class MoveUserRequest
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// The ID of the team.
        /// </summary>
        [JsonPropertyName("team")]
        public MatchTeam Team { get; set; }
    }
}
