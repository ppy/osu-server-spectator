// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
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
        public async Task RoomHasNewPlaylistItemAfterMatchStartWithOneItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);
                Assert.Equal(2, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);

                var oldItem = room.Playlist[0];
                var newItem = room.Playlist[1];

                // Room has new playlist item.
                Assert.NotEqual(oldItem.ID, newItem.ID);
                Assert.False(newItem.Expired);
                Assert.True(oldItem.Expired);
                Assert.Equal(newItem.ID, room.Settings.PlaylistItemId);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == oldItem.ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }

            // And a second time...
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(3, room.Playlist.Count);
                Assert.Equal(3, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);

                var oldItem = room.Playlist[1];
                var newItem = room.Playlist[2];

                // Room has new playlist item.
                Assert.NotEqual(oldItem.ID, newItem.ID);
                Assert.False(newItem.Expired);
                Assert.True(oldItem.Expired);
                Assert.Equal(newItem.ID, room.Settings.PlaylistItemId);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == newItem.ID)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == oldItem.ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(3));
            }
        }

        [Fact]
        public async Task NewPlaylistNotAddedAfterMatchStartWithMultipleItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            // Add another item in free-for-all mode.
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            // Play the first item in host-only mode.
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);
                Assert.Equal(2, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);

                Assert.Equal(room.Playlist[1].ID, room.Settings.PlaylistItemId);
                Assert.True(room.Playlist[0].Expired);

                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[0].ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }

            // Play the second item in host-only mode.
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Room has new playlist item.
                Assert.Equal(3, room.Playlist.Count);
                Assert.Equal(3, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);
                Assert.Equal(room.Playlist[2].ID, room.Settings.PlaylistItemId);
                Assert.True(room.Playlist[1].Expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[2].ID)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[1].ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(3));
            }
        }

        [Fact]
        public async Task ItemLastInQueueWhenChangingToAllPlayersMode()
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

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);

                var firstItem = room.Playlist[0];
                var secondItem = room.Playlist[1];

                // First item (played).
                Assert.NotNull(firstItem);
                Assert.True(firstItem.Expired);

                // Second item (previously current) is the current item.
                Assert.NotNull(secondItem);
                Assert.False(secondItem.Expired);
                Assert.Equal(secondItem.ID, room.Settings.PlaylistItemId);
            }
        }
    }
}
