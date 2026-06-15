// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Makes a new multiplayer room.
    /// </summary>
    [PublicAPI]
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

        /// <summary>
        /// The maximum number of players in the room.
        /// <list type="bullet">
        /// <item>If 0 or missing, the room will allow an unlimited number of participants, but will not have enabled player slots.</item>
        /// <item>If in the range [2, 256] inclusive, the room will have the given number of slots to be occupied by participants.</item>
        /// </list>
        /// </summary>
        [JsonPropertyName("max_participants")]
        public byte MaxParticipants { get; set; }
    }
}
