// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Starts gameplay now or in the immediate future.
    /// </summary>
    [PublicAPI]
    public class StartGameplayRequest
    {
        /// <summary>
        /// Amount of seconds to count down before starting gameplay.
        /// If <see langword="null"/>, gameplay will be started immediately.
        /// </summary>
        [JsonPropertyName("countdown")]
        public int? Countdown { get; set; }
    }
}
