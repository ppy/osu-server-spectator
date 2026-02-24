// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Changes an existing playlist item.
    /// </summary>
    [PublicAPI]
    public class EditPlaylistItemRequest : EditPlaylistItemRequestParameters
    {
        /// <summary>
        /// The ID of the playlist item to change.
        /// </summary>
        [JsonPropertyName("playlist_item_id")]
        public long PlaylistItemId { get; set; }
    }
}
