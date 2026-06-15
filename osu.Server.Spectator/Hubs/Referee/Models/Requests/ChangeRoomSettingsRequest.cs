// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Changes a room's settings.
    /// </summary>
    [PublicAPI]
    public class ChangeRoomSettingsRequest
    {
        /// <summary>
        /// The new name of the room.
        /// Pass <see langword="null"/> to keep the previous one.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The new password to the room.
        /// Pass <see langword="null"/> to keep the previous one.
        /// Pass empty string to set no password.
        /// </summary>
        [JsonPropertyName("password")]
        public string? Password { get; set; }

        /// <summary>
        /// The new match type.
        /// Pass <see langword="null"/> to keep the previous one.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType? MatchType { get; set; }

        /// <summary>
        /// The new maximum number of participants.
        /// Pass <see langword="null"/> to keep the previous one.
        /// Pass 0 to remove the limit.
        /// Pass a number in [2, 128] range to set that as the new limit.
        /// </summary>
        /// <seealso cref="MakeRoomRequest.MaxParticipants"/>
        [JsonPropertyName("max_participants")]
        public byte? MaxParticipants { get; set; }
    }
}
