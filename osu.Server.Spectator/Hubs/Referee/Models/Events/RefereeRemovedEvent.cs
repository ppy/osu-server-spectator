// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user's referee privileges to the room have been revoked.
    /// This may be concurrent with a <see cref="UserKickedEvent"/> if the user was joined to the room at the time of revocation.
    /// </summary>
    public class RefereeRemovedEvent
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
