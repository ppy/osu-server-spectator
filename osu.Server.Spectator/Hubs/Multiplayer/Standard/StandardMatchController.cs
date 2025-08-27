// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer.Standard
{
    /// <summary>
    /// Abstract class that implements the logic for a generic multiplayer room.
    /// </summary>
    public abstract class StandardMatchController : IMatchController
    {
        public const int HOST_PLAYLIST_LIMIT = 50;
        public const int GUEST_PLAYLIST_LIMIT = 3;

        public MultiplayerPlaylistItem CurrentItem => room.Playlist[currentPlaylistItemIndex];

        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;

        private QueueMode queueMode;
        private int currentPlaylistItemIndex;

        protected StandardMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
        {
            this.room = room;
            this.hub = hub;
            this.dbFactory = dbFactory;

            queueMode = room.Settings.QueueMode;
        }

        /// <summary>
        /// Initialises the queue from the database.
        /// </summary>
        public virtual async Task Initialise()
        {
            using (var db = dbFactory.GetInstance())
                await updatePlaylistOrder(db);

            await updateCurrentItem();
        }

        /// <summary>
        /// Updates the queue as a result of a change in the queueing mode.
        /// </summary>
        public virtual async Task HandleSettingsChanged()
        {
            if (queueMode == room.Settings.QueueMode)
                return;

            queueMode = room.Settings.QueueMode;

            using (var db = dbFactory.GetInstance())
            {
                // When changing to host-only mode, ensure that at least one non-expired playlist item exists by duplicating the current item.
                if (room.Settings.QueueMode == QueueMode.HostOnly && room.Playlist.All(item => item.Expired))
                    await addItem(db, CurrentItem.Clone());

                await updatePlaylistOrder(db);
            }

            await updateCurrentItem();
        }

        /// <summary>
        /// Expires the current playlist item and advances to the next one in the order defined by the queueing mode.
        /// </summary>
        public virtual async Task HandleGameplayCompleted()
        {
            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(room.RoomID, CurrentItem.ID);
                room.Playlist[currentPlaylistItemIndex] = (await db.GetPlaylistItemAsync(room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem();

                await hub.NotifyPlaylistItemChanged(room, CurrentItem, true);
                await updatePlaylistOrder(db);

                // In host-only mode, duplicate the playlist item for the next round if no other non-expired items exist.
                if (room.Settings.QueueMode == QueueMode.HostOnly && room.Playlist.All(item => item.Expired))
                    await addItem(db, CurrentItem.Clone());
            }

            await updateCurrentItem();
        }

        public virtual Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleUserJoined(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleUserLeft(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add a playlist item to the room's queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="user">The user adding the item.</param>
        /// <exception cref="NotHostException">If the adding user is not the host in host-only mode.</exception>
        /// <exception cref="InvalidStateException">If the given playlist item is not valid.</exception>
        public virtual async Task AddPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            bool isHostOnly = room.Settings.QueueMode == QueueMode.HostOnly;
            bool isHost = user.Equals(room.Host);

            if (isHostOnly && !isHost)
                throw new NotHostException();

            int limit = isHost ? HOST_PLAYLIST_LIMIT : GUEST_PLAYLIST_LIMIT;

            if (room.Playlist.Count(i => i.OwnerID == user.UserID && !i.Expired) >= limit)
                throw new InvalidStateException($"Can't enqueue more than {limit} items at once.");

            if (item.Freestyle && item.AllowedMods.Any())
                throw new InvalidStateException("Cannot enqueue freestyle item with mods.");

            using (var db = dbFactory.GetInstance())
            {
                var beatmap = await db.GetBeatmapAsync(item.BeatmapID);

                if (beatmap == null)
                    throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");

                if (item.BeatmapChecksum != beatmap.checksum)
                    throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

                if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                    throw new InvalidStateException("Attempted to select an unsupported ruleset.");

                if (beatmap.playmode != 0 && item.RulesetID != beatmap.playmode)
                    throw new InvalidStateException("Attempted to select an invalid beatmap and ruleset combination.");

                item.EnsureModsValid();
                item.OwnerID = user.UserID;
                item.StarRating = beatmap.difficultyrating;

                await addItem(db, item);
                await updateCurrentItem();
            }
        }

        public virtual async Task EditPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            if (item.Freestyle && item.AllowedMods.Any())
                throw new InvalidStateException("Cannot enqueue freestyle item with mods.");

            using (var db = dbFactory.GetInstance())
            {
                var beatmap = await db.GetBeatmapAsync(item.BeatmapID);

                if (beatmap == null)
                    throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");

                if (item.BeatmapChecksum != beatmap.checksum)
                    throw new InvalidStateException("Attempted to add a beatmap which has been modified.");

                if (item.RulesetID < 0 || item.RulesetID > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                    throw new InvalidStateException("Attempted to select an unsupported ruleset.");

                if (beatmap.playmode != 0 && item.RulesetID != beatmap.playmode)
                    throw new InvalidStateException("Attempted to select an invalid beatmap and ruleset combination.");

                item.EnsureModsValid();
                item.OwnerID = user.UserID;
                item.StarRating = beatmap.difficultyrating;

                var existingItem = room.Playlist.SingleOrDefault(i => i.ID == item.ID);

                if (existingItem == CurrentItem)
                {
                    if (room.State != MultiplayerRoomState.Open)
                        throw new InvalidStateException("The current item in the room cannot be edited when currently being played.");
                }

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

                await hub.NotifyPlaylistItemChanged(room, item, existingItem.BeatmapChecksum != item.BeatmapChecksum);
            }
        }

        /// <summary>
        /// Removes a playlist item from the room's queue.
        /// </summary>
        /// <param name="playlistItemId">The item to remove.</param>
        /// <param name="user">The user removing the item.</param>
        public virtual async Task RemovePlaylistItem(long playlistItemId, MultiplayerRoomUser user)
        {
            var item = room.Playlist.FirstOrDefault(item => item.ID == playlistItemId);

            if (item == null)
                throw new InvalidStateException("Item does not exist in the room.");

            if (item == CurrentItem)
            {
                // The current item check is only an optimisation for this condition. It is guaranteed for the single item in the room to be the current item.
                if (UpcomingItems.Count() == 1)
                    throw new InvalidStateException("The only item in the room cannot be removed.");

                if (room.State != MultiplayerRoomState.Open)
                    throw new InvalidStateException("The current item in the room cannot be removed when currently being played.");
            }

            if (item.OwnerID != user.UserID && !user.Equals(room.Host))
                throw new InvalidStateException("Attempted to remove an item which is not owned by the user.");

            if (item.Expired)
                throw new InvalidStateException("Attempted to remove an item which has already been played.");

            using (var db = dbFactory.GetInstance())
            {
                await db.RemovePlaylistItemAsync(room.RoomID, playlistItemId);

                room.Playlist.Remove(item);

                // If either an item indexed earlier in the list was removed or the current item was removed, the index needs to be refreshed.
                // Importantly, this is done before the playlist order is updated since the update requires the current item.
                currentPlaylistItemIndex = room.Playlist.IndexOf(UpcomingItems.First());

                await updatePlaylistOrder(db);
            }

            await updateCurrentItem();

            // It's important for clients to be notified of the removal AFTER settings are changed
            // so that PlaylistItemId always points to a valid item in the playlist.
            await hub.NotifyPlaylistItemRemoved(room, playlistItemId);
        }

        public abstract MatchStartedEventDetail GetMatchDetails();

        private async Task addItem(IDatabaseAccess db, MultiplayerPlaylistItem item)
        {
            // Add the item to the end of the list initially.
            item.PlaylistOrder = ushort.MaxValue;
            item.Expired = false;
            item.PlayedAt = null;
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

            currentPlaylistItemIndex = room.Playlist.IndexOf(nextItem);

            long lastItemID = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = nextItem.ID;

            if (nextItem.ID != lastItemID)
                await hub.NotifySettingsChanged(room, true);
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

                    bool isFirstSet = true;
                    var firstSetOrderByUserId = new Dictionary<int, int>();

                    // Group each user's items in order of addition.
                    var userItemGroups = room.Playlist.Where(item => !item.Expired).OrderBy(item => item.ID).GroupBy(item => item.OwnerID);

                    foreach (IEnumerable<MultiplayerPlaylistItem> set in userItemGroups.Interleave())
                    {
                        // Do some post processing on the set of items to ensure that the order is consistent.
                        if (isFirstSet)
                        {
                            // For the first set, preserve the existing order of items and break ties based on the order in which items were added to the queue.
                            orderedActiveItems.AddRange(set.OrderBy(item => item.PlaylistOrder).ThenBy(item => item.ID));

                            // Store the order of items to be used for all future sets.
                            firstSetOrderByUserId = orderedActiveItems.Select((item, index) => (item, index)).ToDictionary(i => i.item.OwnerID, i => i.index);
                        }
                        else
                        {
                            // For the non-first set, preserve the same ordering of users as in the first set.
                            orderedActiveItems.AddRange(set.OrderBy(i => firstSetOrderByUserId[i.OwnerID]));
                        }

                        isFirstSet = false;
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
                await hub.NotifyPlaylistItemChanged(room, item, false);
            }
        }
    }
}
