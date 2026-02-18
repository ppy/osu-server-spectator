// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A countdown has started in a room.
    /// </summary>
    [PublicAPI]
    public class CountdownStartedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the countdown.
        /// </summary>
        [JsonPropertyName("countdown_id")]
        public long CountdownId { get; set; }

        /// <summary>
        /// Number of seconds (fractional) until the completion of the countdown.
        /// </summary>
        [JsonPropertyName("seconds")]
        public double Seconds { get; set; }

        /// <summary>
        /// The type of the countdown.
        /// </summary>
        [JsonPropertyName("type")]
        public CountdownType Type { get; set; }

        [JsonConstructor]
        public CountdownStartedEvent()
        {
        }

        /// <summary>
        /// Creates a <see cref="CountdownStartedEvent"/> for the supplied <paramref name="roomId"/> and <paramref name="countdown"/>.
        /// </summary>
        /// <remarks>
        /// Some countdowns should not be conveyed to referee clients.
        /// </remarks>
        internal static CountdownStartedEvent? Create(long roomId, MultiplayerCountdown countdown)
        {
            var result = new CountdownStartedEvent
            {
                RoomId = roomId,
                CountdownId = countdown.ID,
                Seconds = countdown.TimeRemaining.TotalSeconds
            };

            switch (countdown)
            {
                case MatchStartCountdown:
                    result.Type = CountdownType.MatchStart;
                    return result;

                case ServerShuttingDownCountdown:
                    result.Type = CountdownType.ServerShuttingDown;
                    return result;

                default:
                    return null;
            }
        }
    }
}
