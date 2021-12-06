// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerAllPlayersRoundRobinQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task RoundRobinOrdering()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            checkCurrentItem(1);

            // User 1 adds an extra item
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            checkCurrentItem(1);
            checkOrder(1, 0);
            checkOrder(2, 1);

            // Item played.
            await runGameplay();
            checkCurrentItem(2);
            checkOrder(2, 0);

            // User 2 adds two items.
            SetUserContext(ContextUser2);

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            checkCurrentItem(2);
            checkOrder(2, 0);
            checkOrder(3, 1);
            checkOrder(4, 2);

            // User 1 adds an item.
            SetUserContext(ContextUser);

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            checkCurrentItem(2);
            checkOrder(2, 0);
            checkOrder(3, 1);
            checkOrder(5, 2);
            checkOrder(4, 3);

            // Gameplay is now run to ensure the ordering doesn't change.
            await runGameplay();
            checkCurrentItem(3);
            await runGameplay();
            checkCurrentItem(5);
            await runGameplay();
            checkCurrentItem(4);

            // After playing the last item, it remains as the current item.
            await runGameplay();
            checkCurrentItem(4);
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

        private void checkCurrentItem(long expectedItemId)
        {
            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(expectedItemId, room.Settings.PlaylistItemId);
            }
        }

        private void checkOrder(long itemId, ushort order)
        {
            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(order, room.Playlist.Single(i => i.ID == itemId).PlaylistOrder);
            }
        }
    }
}
