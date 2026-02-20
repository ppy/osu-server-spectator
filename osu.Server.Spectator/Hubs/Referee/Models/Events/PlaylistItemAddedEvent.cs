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
    public class PlaylistItemAddedEvent : PlaylistItemEventArgs
    {
        [JsonConstructor]
        public PlaylistItemAddedEvent()
        {
        }

        internal PlaylistItemAddedEvent(long roomId, MultiplayerPlaylistItem item)
            : base(roomId, item)
        {
        }
    }
}
