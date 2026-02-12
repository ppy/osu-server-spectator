// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A user has changed their selection of allowed mods.
    /// </summary>
    public class UserModsChangedEvent
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
        /// The mods selected by the user.
        /// Note that these do NOT contain any mods required by the current playlist item.
        /// </summary>
        [JsonPropertyName("mods")]
        public Mod[] UserMods { get; set; } = [];
    }
}
