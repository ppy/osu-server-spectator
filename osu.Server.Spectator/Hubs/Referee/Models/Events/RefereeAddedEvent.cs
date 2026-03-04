// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user has been given referee privileges to the room.
    /// This does not mean the user has joined the room yet; that is indicated by <see cref="UserJoinedEvent"/>.
    /// </summary>
    public class RefereeAddedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The user ID of the referee.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }
    }
}
