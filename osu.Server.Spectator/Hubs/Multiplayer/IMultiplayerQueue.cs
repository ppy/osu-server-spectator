// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public interface IMultiplayerQueue
    {
        MultiplayerPlaylistItem CurrentItem { get; }

        IEnumerable<MultiplayerPlaylistItem> UpcomingItems { get; }

        /// <summary>
        /// Initialises the queue from the database.
        /// </summary>
        Task Initialise();

        /// <summary>
        /// Updates the queue as a result of a change in the queueing mode.
        /// </summary>
        Task UpdateFromQueueModeChange();

        /// <summary>
        /// Expires the current playlist item and advances to the next one in the order defined by the queueing mode.
        /// </summary>
        Task FinishCurrentItem();

        /// <summary>
        /// Add a playlist item to the room's queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="user">The user adding the item.</param>
        /// <exception cref="NotHostException">If the adding user is not the host in host-only mode.</exception>
        /// <exception cref="InvalidStateException">If the given playlist item is not valid.</exception>
        Task AddItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user);

        Task EditItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user);

        /// <summary>
        /// Removes a playlist item from the room's queue.
        /// </summary>
        /// <param name="playlistItemId">The item to remove.</param>
        /// <param name="user">The user removing the item.</param>
        Task RemoveItem(long playlistItemId, MultiplayerRoomUser user);
    }
}
