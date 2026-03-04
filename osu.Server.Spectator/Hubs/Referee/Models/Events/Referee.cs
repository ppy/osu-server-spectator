// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.Json.Serialization;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// Represents a referee in the room.
    /// </summary>
    public class Referee
    {
        /// <summary>
        /// The ID of the referee.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        [JsonConstructor]
        public Referee()
        {
        }

        internal Referee(MultiplayerRoomUser user)
        {
            if (user.Role != MultiplayerRoomUserRole.Referee)
                throw new ArgumentException(nameof(user));

            UserId = user.UserID;
        }
    }
}
