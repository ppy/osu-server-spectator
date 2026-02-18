// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user has changed the selected style for the current playlist item.
    /// Only applicable when the playlist item in question supports freestyle.
    /// </summary>
    [PublicAPI]
    public class UserStyleChangedEvent
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
        /// The ID of the beatmap difficulty selected by the user.
        /// </summary>
        [JsonPropertyName("beatmap_id")]
        public int? BeatmapId { get; set; }

        /// <summary>
        /// The ID of the ruleset selected by the user.
        /// </summary>
        [JsonPropertyName("ruleset_id")]
        public int? RulesetId { get; set; }
    }
}
