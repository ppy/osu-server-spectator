// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Makes a new multiplayer room.
    /// </summary>
    public class MakeRoomRequest
    {
        /// <summary>
        /// The ID of the ruleset to play.
        /// </summary>
        [JsonPropertyName("ruleset_id")]
        public int RulesetId { get; set; }

        /// <summary>
        /// The ID of the beatmap to play.
        /// </summary>
        [JsonPropertyName("beatmap_id")]
        public int BeatmapId { get; set; }

        /// <summary>
        /// The name of the room to create.
        /// </summary>
        [JsonPropertyName("name")]
        public string RoomName { get; set; } = string.Empty;
    }
}
