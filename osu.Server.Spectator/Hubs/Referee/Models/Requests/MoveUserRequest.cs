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
        /// The slot to move the user to.
        /// Pass <see langword="null"/> to keep previous slot.
        /// Only has effect if slots are active; attempting to specify a non-<see langword="null"/> team in a room without a participant count limit will throw.
        /// </summary>
        /// <seealso cref="MakeRoomRequest.MaxParticipants"/>
        /// <seealso cref="Events.RoomSettingsChangedEvent.MaxParticipants"/>
        [JsonPropertyName("slot")]
        public byte? Slot { get; set; }

        /// <summary>
        /// The team to move the user to.
        /// Pass <see langword="null"/> to keep previous team.
        /// Only has effect in <see cref="MatchType.TeamVersus"/>; attempting to specify a non-<see langword="null"/> team in other match types will throw.
        /// </summary>
        [JsonPropertyName("team")]
        public MatchTeam? Team { get; set; }
    }
}
