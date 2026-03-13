// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// The room's <see cref="MatchType"/>-related state has changed.
    /// </summary>
    [PublicAPI]
    public class MatchStateChangedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The updated state of the room.
        /// </summary>
        [JsonPropertyName("state")]
        public MatchState State { get; set; } = null!;
    }
}
