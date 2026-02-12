// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// A game in a multiplayer room has been aborted mid-gameplay.
    /// </summary>
    public class MatchAbortedEvent
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        public long RoomId { get; init; }

        /// <summary>
        /// The ID of the playlist item which was being played.
        /// </summary>
        public long PlaylistItemId { get; init; }
    }
}
