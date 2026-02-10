// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Responses
{
    /// <summary>
    /// Contains information about a room that was just joined.
    /// </summary>
    public class RoomJoinedResponse
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the chat channel for the joined multiplayer room.
        /// </summary>
        [JsonPropertyName("chat_channel_id")]
        public int ChatChannelId { get; set; }

        /// <summary>
        /// The name of the room.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The password to the room.
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The match type of the joined room.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType Type { get; set; }

        [JsonConstructor]
        public RoomJoinedResponse()
        {
        }

        public RoomJoinedResponse(MultiplayerRoom room)
        {
            RoomId = room.RoomID;
            ChatChannelId = room.ChannelID;
            Name = room.Settings.Name;
            Password = room.Settings.Password;
            Type = (MatchType)room.Settings.MatchType;
        }
    }
}
