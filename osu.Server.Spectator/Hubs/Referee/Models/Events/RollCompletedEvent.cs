// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A roll has completed in the room.
    /// </summary>
    [PublicAPI]
    public class RollCompletedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the user who initiated the roll.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// The maximum possible result of the roll. In the range [2, 100] inclusive.
        /// </summary>
        [JsonPropertyName("max")]
        public uint Max { get; set; }

        /// <summary>
        /// The actual result of the roll. In the range [1, <see cref="Max"/>] inclusive.
        /// </summary>
        [JsonPropertyName("result")]
        public uint Result { get; set; }

        [JsonConstructor]
        public RollCompletedEvent()
        {
        }

        internal RollCompletedEvent(long roomId, RollEvent e)
        {
            RoomId = roomId;
            UserId = e.UserID;
            Max = e.Max;
            Result = e.Result;
        }
    }
}
