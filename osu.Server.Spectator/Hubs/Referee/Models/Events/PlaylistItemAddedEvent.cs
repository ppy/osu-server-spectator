// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A playlist item has been added to a multiplayer room.
    /// </summary>
    [PublicAPI]
    public class PlaylistItemAddedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The playlist item that was added.
        /// </summary>
        [JsonPropertyName("playlist_item")]
        public PlaylistItem PlaylistItem { get; set; } = null!;

        [JsonConstructor]
        public PlaylistItemAddedEvent()
        {
        }

        internal PlaylistItemAddedEvent(long roomId, MultiplayerPlaylistItem item)
        {
            RoomId = roomId;
            PlaylistItem = new PlaylistItem(item);
        }
    }
}
