// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Responses
{
    /// <summary>
    /// Contains the list of all rooms that the caller is a referee in.
    /// </summary>
    public class ListRoomsResponse
    {
        /// <summary>
        /// The list of IDs of rooms in which the caller is a referee.
        /// </summary>
        [JsonPropertyName("room_ids")]
        public IEnumerable<long> RoomIDs { get; set; } = [];
    }
}
