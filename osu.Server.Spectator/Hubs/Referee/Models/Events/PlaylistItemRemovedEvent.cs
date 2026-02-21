// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A playlist item has been removed from a multiplayer room.
    /// </summary>
    [PublicAPI]
    public class PlaylistItemRemovedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the playlist item.
        /// </summary>
        [JsonPropertyName("playlist_item_id")]
        public long PlaylistItemId { get; set; }
    }
}
