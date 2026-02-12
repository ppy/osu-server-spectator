// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user in a room has changed status.
    /// </summary>
    public class UserStatusChangedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the user.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// The user's new status.
        /// </summary>
        [JsonPropertyName("status")]
        public MatchUserStatus Status { get; set; }
    }
}
