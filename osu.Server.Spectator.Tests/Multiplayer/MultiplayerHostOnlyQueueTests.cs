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
    public class MultiplayerHostOnlyQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task GuestsCannotAddItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<NotHostException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));
        }

        [Fact]
        public async Task ItemsCannotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(0));
        }

        [Fact]
        public async Task AddingItemUpdatesExisting()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

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
                Database.Verify(db => db.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Never);
                Receiver.Verify(r => r.PlaylistItemChanged(newItem), Times.Once);
            }
        }

        [Fact]
        public async Task RoomHasNewPlaylistItemAfterMatchStart()
        {
            long firstItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            long secondItemId;

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var newItem = room.QueueImplementation.CurrentItem;
                secondItemId = newItem.ID;

                // Room has new playlist item.
                Assert.NotEqual(firstItemId, newItem.ID);
                Assert.Equal(newItem.ID, room.Settings.PlaylistItemId);
                Assert.False(newItem.Expired);

                // Previous item is expired.
                Assert.True((await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, firstItemId))!.expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == firstItemId && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }

            // And a second time...
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var newItem = room.QueueImplementation.CurrentItem;

                // Room has new playlist item.
                Assert.NotEqual(secondItemId, newItem.ID);
                Assert.Equal(newItem.ID, room.Settings.PlaylistItemId);

                // Previous item is expired.
                Assert.True((await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, secondItemId))!.expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == secondItemId && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task AddingItemDoesNotAffectPastItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long firstItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

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

                var firstItem = (await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, firstItemId))!;
                var currentItem = room.QueueImplementation.CurrentItem;

                // Current item changed.
                Assert.Equal(newItem.BeatmapID, currentItem.BeatmapID);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == currentItem.ID && i.BeatmapID == newItem.BeatmapID)), Times.Once);

                // Previous item unchanged.
                Assert.Equal(1234, firstItem.beatmap_id);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == firstItem.id && i.BeatmapID != 1234)), Times.Never);
            }
        }

        [Fact]
        public async Task ItemLastInQueueWhenChangingToFreeForAllMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

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
            }
        }
    }
}
