// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user has joined a refereed room.
    /// </summary>
    [PublicAPI]
    public class UserJoinedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public required long RoomId { get; init; }

        /// <summary>
        /// The ID of the user who joined.
        /// </summary>
        [JsonPropertyName("user_id")]
        public required long UserId { get; init; }
    }
}
