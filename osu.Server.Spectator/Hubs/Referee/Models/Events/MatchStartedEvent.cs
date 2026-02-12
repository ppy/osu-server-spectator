// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A game has started in a multiplayer room.
    /// </summary>
    public class MatchStartedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; init; }

        /// <summary>
        /// The ID of the playlist item.
        /// </summary>
        [JsonPropertyName("playlist_item_id")]
        public long PlaylistItemId { get; init; }

        /// <summary>
        /// The type of match.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType MatchType { get; init; }

        /// <summary>
        /// Mapping containing team membership of players in the room.
        /// Only applicable in <see cref="Models.MatchType.TeamVersus"/>.
        /// </summary>
        public Dictionary<int, MatchTeam>? Teams { get; init; }
    }
}
