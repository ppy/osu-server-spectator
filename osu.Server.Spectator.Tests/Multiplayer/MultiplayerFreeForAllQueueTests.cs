// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Queueing;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerFreeForAllQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task AddingItemAppendsToQueue()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            var newItem = new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(playlistItemId, room.Settings.PlaylistItemId);
                Database.Verify(db => db.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Never);
                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Receiver.Verify(r => r.PlaylistItemAdded(newItem), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()), Times.Once);
            }
        }

        [Fact]
        public async Task CompletingItemExpiresAndDoesNotAddNewItems()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var currentItem = room.QueueImplementation.CurrentItem;

                // Room maintains playlist item.
                Assert.Equal(currentItem.ID, room.Settings.PlaylistItemId);
                Assert.True(currentItem.Expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == currentItem.ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task CanNotStartExpiredItem()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task NewItemImmediatelySelectedWhenAllItemsExpired()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            var newItem = new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var currentItem = room.QueueImplementation.CurrentItem;

                // New playlist item selected.
                Assert.Equal(newItem.BeatmapID, currentItem.BeatmapID);
                Assert.Equal(currentItem.ID, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task AllNonExpiredItemsExceptCurrentRemovedWhenChangingToHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // First item (played) still exists in the database.
                var firstItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 1);
                Assert.NotNull(firstItem);
                Assert.True(firstItem!.expired);

                // Second item (previously current) still exists and is the current item.
                var secondItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 2);
                Assert.NotNull(secondItem);
                Assert.False(secondItem!.expired);
                Assert.Equal(secondItem.id, room.Settings.PlaylistItemId);

                // Third item (future item) removed.
                var thirdItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 3);
                Assert.Null(thirdItem);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemRemoved(It.Is<long>(id => id == 3)), Times.Once);
            }
        }

        [Fact]
        public async Task OneNonExpiredItemExistsWhenChangingToHostOnlyMode()
        {
            long firstItem = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // First item (played) still exists in the database.
                var currentItem = room.QueueImplementation.CurrentItem;
                Assert.NotEqual(firstItem, currentItem.ID);
                Assert.False(currentItem.Expired);
            }
        }
    }
}
