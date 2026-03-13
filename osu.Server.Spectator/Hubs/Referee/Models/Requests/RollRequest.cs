// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Initiates a random roll in the room.
    /// </summary>
    [PublicAPI]
    public class RollRequest
    {
        /// <summary>
        /// The maximum possible result of the roll. In the range [2, 100] inclusive.
        /// Defaults to 100 if omitted.
        /// </summary>
        [JsonPropertyName("max")]
        public uint? Max { get; set; }
    }
}
