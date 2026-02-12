// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A room's settings have changed.
    /// </summary>
    public class RoomSettingsChangedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The new name of the room.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The new password of the room.
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The new type of the room.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType Type { get; set; }

        /// <summary>
        /// The ID of the current playlist item in the room.
        /// </summary>
        [JsonPropertyName("playlist_item_id")]
        public long PlaylistItemId { get; set; }

        [JsonConstructor]
        public RoomSettingsChangedEvent()
        {
        }

        public RoomSettingsChangedEvent(long roomId, MultiplayerRoomSettings settings)
        {
            RoomId = roomId;
            Name = settings.Name;
            Password = settings.Password;
            Type = (MatchType)settings.MatchType;
            PlaylistItemId = settings.PlaylistItemId;
        }
    }
}
