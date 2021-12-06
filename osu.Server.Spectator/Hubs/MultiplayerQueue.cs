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
        private readonly IMultiplayerServerMatchCallbacks hub;

        private IDatabaseFactory? dbFactory;
        private int currentIndex;

        public MultiplayerQueue(ServerMultiplayerRoom room, IMultiplayerServerMatchCallbacks hub)
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

                await hub.OnPlaylistItemChanged(room, CurrentItem);
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

            if (room.Settings.QueueMode != QueueMode.HostOnly && room.Playlist.Count(i => i.OwnerID == user.UserID && !i.Expired) >= PER_USER_LIMIT)
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

                switch (room.Settings.QueueMode)
                {
                    case QueueMode.HostOnly:
                        // In host-only mode, the current item is re-used.
                        item.ID = CurrentItem.ID;
                        item.PlaylistOrder = CurrentItem.PlaylistOrder;

                        await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                        room.Playlist[currentIndex] = item;

                        await hub.OnPlaylistItemChanged(room, item);
                        break;

                    default:
                        await addItem(db, item);

                        // The current item can change as a result of an item being added. For example, if all items earlier in the queue were expired.
                        await updateCurrentItem();
                        break;
                }
            }
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
            await hub.OnPlaylistItemAdded(room, item);

            await updatePlaylistOrder(db);
        }

        /// <summary>
        /// Updates <see cref="CurrentItem"/> and the playlist item ID stored in the room's settings.
        /// </summary>
        private async Task updateCurrentItem()
        {
            // Pick the next non-expired playlist item by playlist order, or default to the most-recently-expired item.
            MultiplayerPlaylistItem nextItem = room.Playlist.Where(i => !i.Expired).OrderBy(i => i.PlaylistOrder).FirstOrDefault()
                                               ?? room.Playlist.OrderByDescending(i => i.PlayedAt).First();

            currentIndex = room.Playlist.IndexOf(nextItem);

            long lastItemID = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = nextItem.ID;

            if (nextItem.ID != lastItemID)
                await hub.OnMatchSettingsChanged(room);
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
                    orderedActiveItems = new List<MultiplayerPlaylistItem>();

                    // Todo: This could probably be more efficient, likely at the cost of increased complexity.
                    // Number of "expired" or "used" items per player.
                    Dictionary<int, int> perUserCounts = room.Playlist
                                                             .GroupBy(item => item.OwnerID)
                                                             .ToDictionary(group => group.Key, group => group.Count(item => item.Expired));

                    // We'll run a simulation over all items which are not expired ("unprocessed"). Expired items will not have their ordering updated.
                    List<MultiplayerPlaylistItem> unprocessedItems = room.Playlist.Where(item => !item.Expired).ToList();

                    // In every iteration of the simulation, pick the first available item from the user with the lowest number of items in the queue to add to the result set.
                    // If multiple users have the same number of items in the queue, then the item with the lowest ID is chosen.
                    while (unprocessedItems.Count > 0)
                    {
                        MultiplayerPlaylistItem candidateItem = unprocessedItems
                                                                .OrderBy(item => perUserCounts[item.OwnerID])
                                                                .ThenBy(item => item.ID)
                                                                .First();

                        unprocessedItems.Remove(candidateItem);
                        orderedActiveItems.Add(candidateItem);

                        perUserCounts[candidateItem.OwnerID]++;
                    }

                    break;
            }

            for (int i = 0; i < orderedActiveItems.Count; i++)
            {
                var item = orderedActiveItems[i];

                if (item.PlaylistOrder == i)
                    continue;

                item.PlaylistOrder = (ushort)i;

                await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                await hub.OnPlaylistItemChanged(room, item);
            }
        }
    }
}
