// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerQueue
    {
        public const int PER_USER_LIMIT = 3;

        public MultiplayerPlaylistItem CurrentItem => room.Playlist[currentIndex];

        private readonly ServerMultiplayerRoom room;
        private readonly MultiplayerHubContext hub;

        private IDatabaseFactory? dbFactory;
        private int currentIndex;

        public MultiplayerQueue(ServerMultiplayerRoom room, MultiplayerHubContext hub)
        {
            this.room = room;
            this.hub = hub;
        }

        /// <summary>
        /// Initialises the queue from the database.
        /// </summary>
        public async Task Initialise(IDatabaseFactory dbFactory)
        {
            this.dbFactory = dbFactory;

            using (var db = dbFactory.GetInstance())
            {
                foreach (var item in await db.GetAllPlaylistItemsAsync(room.RoomID))
                    room.Playlist.Add(await item.ToMultiplayerPlaylistItem(db));

                await updatePlaylistOrder(db);
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Updates the queue as a result of a change in the queueing mode.
        /// </summary>
        public async Task UpdateFromQueueModeChange()
        {
            if (dbFactory == null) throw new InvalidOperationException($"Call {nameof(Initialise)} first.");

            using (var db = dbFactory.GetInstance())
            {
                // When changing to host-only mode, ensure that at least one non-expired playlist item exists by duplicating the current item.
                if (room.Settings.QueueMode == QueueMode.HostOnly && room.Playlist.All(item => item.Expired))
                    await duplicateCurrentItem(db);

                await updatePlaylistOrder(db);
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Expires the current playlist item and advances to the next one in the order defined by the queueing mode.
        /// </summary>
        public async Task FinishCurrentItem()
        {
            if (dbFactory == null) throw new InvalidOperationException($"Call {nameof(Initialise)} first.");

            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(room.RoomID, CurrentItem.ID);
                room.Playlist[currentIndex] = await (await db.GetPlaylistItemAsync(room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem(db);

                await hub.NotifyPlaylistItemChanged(room, CurrentItem);
                await updatePlaylistOrder(db);

                // In host-only mode, duplicate the playlist item for the next round if no other non-expired items exist.
                if (room.Settings.QueueMode == QueueMode.HostOnly && room.Playlist.All(item => item.Expired))
                    await duplicateCurrentItem(db);
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Add a playlist item to the room's queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="user">The user adding the item.</param>
        /// <exception cref="NotHostException">If the adding user is not the host in host-only mode.</exception>
        /// <exception cref="InvalidStateException">If the given playlist item is not valid.</exception>
        public async Task AddItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            if (dbFactory == null) throw new InvalidOperationException($"Call {nameof(Initialise)} first.");

            if (room.Settings.QueueMode == QueueMode.HostOnly && !user.Equals(room.Host))
                throw new NotHostException();

            if (room.Playlist.Count(i => i.OwnerID == user.UserID && !i.Expired) >= PER_USER_LIMIT)
                throw new InvalidStateException($"Can't enqueue more than {PER_USER_LIMIT} items at once.");

            using (var db = dbFactory.GetInstance())
            {
                string? beatmapChecksum = await db.GetBeatmapChecksumAsync(item.BeatmapID);

                if (beatmapChecksum == null)
                    throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");

                if (item.BeatmapChecksum != beatmapChecksum)
                    throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

                if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                    throw new InvalidStateException("Attempted to select an unsupported ruleset.");

                item.EnsureModsValid();
                item.OwnerID = user.UserID;

                await addItem(db, item);
                await updateCurrentItem();
            }
        }

        public async Task EditItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            if (dbFactory == null) throw new InvalidOperationException($"Call {nameof(Initialise)} first.");

            using (var db = dbFactory.GetInstance())
            {
                string? beatmapChecksum = await db.GetBeatmapChecksumAsync(item.BeatmapID);

                if (beatmapChecksum == null)
                    throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");

                if (item.BeatmapChecksum != beatmapChecksum)
                    throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

                if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                    throw new InvalidStateException("Attempted to select an unsupported ruleset.");

                item.EnsureModsValid();
                item.OwnerID = user.UserID;

                var existingItem = room.Playlist.SingleOrDefault(i => i.ID == item.ID);

                if (existingItem == null)
                    throw new InvalidStateException("Attempted to change an item that doesn't exist.");

                if (existingItem.OwnerID != user.UserID && !user.Equals(room.Host))
                    throw new InvalidStateException("Attempted to change an item which is not owned by the user.");

                if (existingItem.Expired)
                    throw new InvalidStateException("Attempted to change an item which has already been played.");

                // Ensure the playlist order doesn't change.
                item.PlaylistOrder = existingItem.PlaylistOrder;

                await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                room.Playlist[room.Playlist.IndexOf(existingItem)] = item;

                await hub.NotifyPlaylistItemChanged(room, item);
            }
        }

        /// <summary>
        /// Removes a playlist item from the room's queue.
        /// </summary>
        /// <param name="playlistItemId">The item to remove.</param>
        /// <param name="user">The user removing the item.</param>
        public async Task RemoveItem(long playlistItemId, MultiplayerRoomUser user)
        {
            if (dbFactory == null) throw new InvalidOperationException($"Call {nameof(Initialise)} first.");

            var item = room.Playlist.FirstOrDefault(item => item.ID == playlistItemId);

            if (item == null)
                throw new InvalidStateException("Item does not exist in the room.");

            if (item == CurrentItem)
                throw new InvalidStateException("The room's current item cannot be removed.");

            if (item.OwnerID != user.UserID && !user.Equals(room.Host))
                throw new InvalidStateException("Attempted to remove an item which is not owned by the user.");

            if (item.Expired)
                throw new InvalidStateException("Attempted to remove an item which has already been played.");

            using (var db = dbFactory.GetInstance())
                await db.RemovePlaylistItemAsync(room.RoomID, playlistItemId);

            room.Playlist.Remove(item);
            await hub.NotifyPlaylistItemRemoved(room, playlistItemId);

            await updateCurrentItem();
        }

        /// <summary>
        /// Duplicates <see cref="CurrentItem"/> into the database.
        /// </summary>
        /// <param name="db">The database connection.</param>
        private async Task duplicateCurrentItem(IDatabaseAccess db) => await addItem(db, new MultiplayerPlaylistItem
        {
            OwnerID = CurrentItem.OwnerID,
            BeatmapID = CurrentItem.BeatmapID,
            BeatmapChecksum = CurrentItem.BeatmapChecksum,
            RulesetID = CurrentItem.RulesetID,
            AllowedMods = CurrentItem.AllowedMods,
            RequiredMods = CurrentItem.RequiredMods
        });

        private async Task addItem(IDatabaseAccess db, MultiplayerPlaylistItem item)
        {
            item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));

            room.Playlist.Add(item);
            await hub.NotifyPlaylistItemAdded(room, item);

            await updatePlaylistOrder(db);
        }

        public IEnumerable<MultiplayerPlaylistItem> UpcomingItems => room.Playlist.Where(i => !i.Expired).OrderBy(i => i.PlaylistOrder);

        /// <summary>
        /// Updates <see cref="CurrentItem"/> and the playlist item ID stored in the room's settings.
        /// </summary>
        private async Task updateCurrentItem()
        {
            // Pick the next non-expired playlist item by playlist order, or default to the most-recently-expired item.
            MultiplayerPlaylistItem nextItem = UpcomingItems.FirstOrDefault() ?? room.Playlist.OrderByDescending(i => i.PlayedAt).First();

            currentIndex = room.Playlist.IndexOf(nextItem);

            long lastItemID = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = nextItem.ID;

            if (nextItem.ID != lastItemID)
                await hub.NotifySettingsChanged(room);
        }

        /// <summary>
        /// Updates the order of items in the playlist according to the queueing mode.
        /// </summary>
        private async Task updatePlaylistOrder(IDatabaseAccess db)
        {
            List<MultiplayerPlaylistItem> orderedActiveItems;

            switch (room.Settings.QueueMode)
            {
                default:
                    orderedActiveItems = room.Playlist.Where(item => !item.Expired).OrderBy(item => item.ID).ToList();
                    break;

                case QueueMode.AllPlayersRoundRobin:
                    var itemsByPriority = new List<(MultiplayerPlaylistItem item, int priority)>();

                    // Assign a priority for items from each user, starting from 0 and increasing in order which the user added the items.
                    foreach (var group in room.Playlist.Where(item => !item.Expired).OrderBy(item => item.ID).GroupBy(item => item.OwnerID))
                    {
                        int priority = 0;
                        itemsByPriority.AddRange(group.Select(item => (item, priority++)));
                    }

                    orderedActiveItems = itemsByPriority
                                         // Order by each user's priority.
                                         .OrderBy(i => i.priority)
                                         // Many users will have the same priority of items, so attempt to break the tie by maintaining previous ordering.
                                         // Suppose there are two users: User1 and User2. User1 adds two items, and then User2 adds a third. If the previous order is not maintained,
                                         // then after playing the first item by User1, their second item will become priority=0 and jump to the front of the queue (because it was added first).
                                         .ThenBy(i => i.item.PlaylistOrder)
                                         // If there are still ties (normally shouldn't happen), break ties by making items added earlier go first.
                                         // This could happen if e.g. the item orders get reset.
                                         .ThenBy(i => i.item.ID)
                                         .Select(i => i.item)
                                         .ToList();

                    break;
            }

            for (int i = 0; i < orderedActiveItems.Count; i++)
            {
                var item = orderedActiveItems[i];

                if (item.PlaylistOrder == i)
                    continue;

                item.PlaylistOrder = (ushort)i;

                await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                await hub.NotifyPlaylistItemChanged(room, item);
            }
        }
    }
}
