// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerAllPlayersQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task AddingItemAppendsToQueue()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            var newItem = new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(playlistItemId, room.Settings.PlaylistItemId);
                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Receiver.Verify(r => r.PlaylistItemAdded(newItem), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task CompletingItemExpiresAndDoesNotAddNewItems()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserMarkReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist);
                Assert.Single(await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID));

                // Room maintains playlist item.
                Assert.Equal(room.Playlist[0].ID, room.Settings.PlaylistItemId);
                Assert.True(room.Playlist[0].Expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == room.Playlist[0].ID && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task NewItemImmediatelySelectedWhenAllItemsExpired()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserMarkReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            var newItem = new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var secondItem = room.Playlist[1];

                // New playlist item selected.
                Assert.Equal(secondItem.ID, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task ItemsNotClearedWhenChangingToHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap { checksum = "4444" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

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

            await MarkCurrentUserMarkReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // All items still present in the room.
                Assert.Equal(3, room.Playlist.Count);
                Assert.Equal(3, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);
            }
        }

        [Fact]
        public async Task OneNonExpiredItemExistsWhenChangingToHostOnlyMode()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserMarkReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);
                Assert.Equal(2, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);

                var firstItem = room.Playlist[0];
                var secondItem = room.Playlist[1];

                // First item (played).
                Assert.NotEqual(firstItem.ID, room.Settings.PlaylistItemId);
                Assert.True(firstItem.Expired);

                // Second item (new).
                Assert.Equal(secondItem.ID, room.Settings.PlaylistItemId);
                Assert.False(secondItem.Expired);
            }
        }

        [Fact]
        public async Task HostMayAddManyItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            for (int i = 1; i < MultiplayerQueue.PER_USER_LIMIT; i++)
                await addItem();

            // Host should be allowed to add many items even in non-host-only queue modes.
            await addItem();
            await addItem();
            await addItem();

            async Task addItem() => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });
        }

        [Fact]
        public async Task UserMayOnlyAddLimitedNumberOfItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            // Play initial beatmap to clear the queue.
            await playBeatmap();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            // Non-host user should be able to add items up to a defined limit.
            for (int i = 0; i < MultiplayerQueue.PER_USER_LIMIT; i++)
                await addItem();

            // User is not allowed to add more items.
            await Assert.ThrowsAsync<InvalidStateException>(addItem);

            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            SetUserContext(ContextUser);

            await MarkCurrentUserMarkReadyAndAvailable();
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser, ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await addItem();
            await Assert.ThrowsAsync<InvalidStateException>(addItem);

            async Task addItem() => await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            async Task playBeatmap()
            {
                SetUserContext(ContextUser);

                await MarkCurrentUserMarkReadyAndAvailable();
                await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
                await Hub.StartMatch();
                await LoadAndFinishGameplay(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Idle);
            }
        }
    }
}
