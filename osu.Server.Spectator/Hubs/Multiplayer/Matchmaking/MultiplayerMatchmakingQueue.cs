// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MultiplayerMatchmakingQueue : IMultiplayerQueue
    {
        public MultiplayerPlaylistItem CurrentItem => room.CurrentPlaylistItem;

        public IEnumerable<MultiplayerPlaylistItem> UpcomingItems => [];

        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;

        public MultiplayerMatchmakingQueue(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
        {
            this.room = room;
            this.hub = hub;
            this.dbFactory = dbFactory;

            // Just to have a valid value.
            room.Settings.PlaylistItemId = room.Playlist[0].ID;
        }

        Task IMultiplayerQueue.Initialise()
            => Task.CompletedTask;

        Task IMultiplayerQueue.UpdateFromQueueModeChange()
            => Task.CompletedTask;

        async Task IMultiplayerQueue.FinishCurrentItem()
        {
            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(room.RoomID, CurrentItem.ID);
                room.Playlist[room.Playlist.IndexOf(CurrentItem)] = (await db.GetPlaylistItemAsync(room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem();
                await hub.NotifyPlaylistItemChanged(room, CurrentItem, true);

                // Add a non-expired duplicate of the current item back to the room.
                MultiplayerPlaylistItem newItem = CurrentItem.Clone();
                newItem.Expired = false;
                newItem.PlayedAt = null;
                newItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, newItem));
                room.Playlist.Add(newItem);
                await hub.NotifyPlaylistItemAdded(room, newItem);
            }
        }

        Task IMultiplayerQueue.AddItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
            => Task.FromException(new InvalidStateException("Adding items is prohibited in matchmaking rooms."));

        Task IMultiplayerQueue.EditItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
            => Task.FromException(new InvalidStateException("Editing items is prohibited in matchmaking rooms."));

        Task IMultiplayerQueue.RemoveItem(long playlistItemId, MultiplayerRoomUser user)
            => Task.FromException(new InvalidStateException("Removing items is prohibited in matchmaking rooms."));
    }
}
