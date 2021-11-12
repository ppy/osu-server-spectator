// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Queueing;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerFairPlayQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task PicksFromLeastPlayedUser()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");
            Database.Setup(d => d.GetBeatmapChecksumAsync(5555)).ReturnsAsync("5555");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FairRotate });

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            SetUserContext(ContextUser2);

            await Hub.JoinRoom(ROOM_ID);

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 5555,
                BeatmapChecksum = "5555"
            });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 1 selected (user 1)
                Assert.Equal(1, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 4 selected (user 2)
                Assert.Equal(4, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 2 selected (user 1)
                Assert.Equal(2, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 3 selected (user 1). User 2 has no more items available.
                Assert.Equal(3, room.Settings.PlaylistItemId);
            }
        }

        [Fact]
        public async Task CurrentItemUpdatedWhenChangingToAndFromFreeForAll()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");

            // The room is free-for-all initially.
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });
            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser2);

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // After playing the first item, the second item (by player 1) is selected.
                Assert.Equal(2, room.Settings.PlaylistItemId);
            }

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FairRotate });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // After changing to fair-play mode, we expect the third item (by player 2) to be selected.
                Assert.Equal(3, room.Settings.PlaylistItemId);
            }

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // After changing back to free-for-all mode, we expect the second item (by player 1) to be selected.
                Assert.Equal(2, room.Settings.PlaylistItemId);
            }
        }

        private async Task runGameplay()
        {
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            SetUserContext(ContextUser);
        }
    }
}
