// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user has been kicked from a refereed room.
    /// </summary>
    public class UserKickedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public required long RoomId { get; init; }

        /// <summary>
        /// The ID of the user who was kicked.
        /// </summary>
        [JsonPropertyName("kicked_user_id")]
        public required long KickedUserId { get; init; }

        /// <summary>
        /// The ID of the user who performed the kick.
        /// </summary>
        [JsonPropertyName("kicking_user_id")]
        public required long KickingUserId { get; init; }
    }
}
