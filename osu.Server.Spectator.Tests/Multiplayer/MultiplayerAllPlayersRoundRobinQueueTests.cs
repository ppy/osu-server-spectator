// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerAllPlayersRoundRobinQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task RoundRobinOrderingWithGameplay()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await checkCurrentItem(1);

            // User 1 adds an extra item
            await addItem();

            await checkCurrentItem(1);
            await checkOrder(1, 2);

            // Item played.
            await runGameplay();
            await checkCurrentItem(2);
            await checkOrder(2);

            // User 2 adds two items.
            SetUserContext(ContextUser2);
            await addItem();
            await addItem();

            await checkCurrentItem(2);
            await checkOrder(2, 3, 4);

            // User 1 adds an item.
            SetUserContext(ContextUser);
            await addItem();

            await checkCurrentItem(2);
            await checkOrder(2, 3, 5, 4);

            // Gameplay is now run to ensure the ordering doesn't change.
            await runGameplay();
            await checkCurrentItem(3);
            await runGameplay();
            await checkCurrentItem(5);
            await runGameplay();
            await checkCurrentItem(4);

            // After playing the last item, it remains as the current item.
            await runGameplay();
            await checkCurrentItem(4);
        }

        [Fact]
        public async Task RoundRobinOrderingWithManyUsers()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);
            CreateUser(4, out var contextUser4, out _);

            // ---
            // User 1 joins and begins adding items.
            // ---

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   | (new)
            await checkOrder(1);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  2   |  1   | (new)
            await addItem();
            await checkOrder(1, 2);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  2   |  1   |
            // |  3   |  1   | (new)
            await addItem();
            await checkOrder(1, 2, 3);

            // ---
            // User 2 joins and begins adding items.
            // ---

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   | (new)
            // |  2   |  1   |
            // |  3   |  1   |
            await addItem();
            await checkOrder(1, 4, 2, 3);

            // ---
            // User 3 joins and begins adding items.
            // ---

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   | (new)
            // |  2   |  1   |
            // |  3   |  1   |
            await addItem();
            await checkOrder(1, 4, 5, 2, 3);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  2   |  1   |
            // |  6   |  3   | (new)
            // |  3   |  1   |
            await addItem();
            await checkOrder(1, 4, 5, 2, 6, 3);

            // ---
            // User 2 adds more items.
            // ---

            SetUserContext(contextUser2);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  2   |  1   |
            // |  7   |  2   | (new)
            // |  6   |  3   |
            // |  3   |  1   |
            await addItem();
            await checkOrder(1, 4, 5, 2, 7, 6, 3);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  2   |  1   |
            // |  7   |  2   |
            // |  6   |  3   |
            // |  3   |  1   |
            // |  8   |  2   | (new)
            await addItem();
            await checkOrder(1, 4, 5, 2, 7, 6, 3, 8);

            // ---
            // User 4 joins and begins adding items.
            // ---

            SetUserContext(contextUser4);
            await Hub.JoinRoom(ROOM_ID);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  9   |  4   | (new)
            // |  2   |  1   |
            // |  7   |  2   |
            // |  6   |  3   |
            // |  3   |  1   |
            // |  8   |  2   |
            await addItem();
            await checkOrder(1, 4, 5, 9, 2, 7, 6, 3, 8);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  9   |  4   |
            // |  2   |  1   |
            // |  7   |  2   |
            // |  6   |  3   |
            // |  10  |  4   | (new)
            // |  3   |  1   |
            // |  8   |  2   |
            await addItem();
            await checkOrder(1, 4, 5, 9, 2, 7, 6, 10, 3, 8);

            // | item | user |
            // | ---- | ---- |
            // |  1   |  1   |
            // |  4   |  2   |
            // |  5   |  3   |
            // |  9   |  4   |
            // |  2   |  1   |
            // |  7   |  2   |
            // |  6   |  3   |
            // |  10  |  4   |
            // |  3   |  1   |
            // |  8   |  2   |
            // |  11  |  4   | (new)
            await addItem();
            await checkOrder(1, 4, 5, 9, 2, 7, 6, 10, 3, 8, 11);
        }

        [Fact]
        public async Task OrderUpdatedOnRemoval()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });
            await addItem();
            await addItem();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await addItem();
            await addItem();
            await addItem();

            // Items should be initially interleaved.
            await checkOrder(1, 4, 2, 5, 3, 6);

            // Remove item with id 4.
            await Hub.RemovePlaylistItem(4);
            await checkOrder(1, 5, 2, 6, 3);

            // Remove item with id 5.
            await Hub.RemovePlaylistItem(5);
            await checkOrder(1, 6, 2, 3);
        }

        [Fact]
        public async Task RemoveWhenCurrentItemIsAtEndOfList()
        {
            CreateUser(1, out var user1Ctx, out _);
            CreateUser(2, out var user2Ctx, out _);

            SetUserContext(user1Ctx);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayersRoundRobin });

            // Queue: [ 1 ]
            // List: [ 1 ]

            // Add two more items.
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 2, BeatmapChecksum = "checksum" });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 3, BeatmapChecksum = "checksum" });

            // Queue: [ 1, 2, 3 ]
            // List: [ 1, 2, 3 ]

            // Join another user and add one more item.
            SetUserContext(user2Ctx);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem { BeatmapID = 4, BeatmapChecksum = "checksum" });

            // Queue: [ 1, 4, 2, 3 ]
            // List: [ 1, 2, 3, 4 ]

            // Run gameplay.
            SetUserContext(user1Ctx);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(user2Ctx);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(user1Ctx);

            await Hub.StartMatch();
            await LoadAndFinishGameplay(user1Ctx, user2Ctx);
            await Hub.ChangeState(MultiplayerUserState.Idle);
            SetUserContext(user2Ctx);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            // Queue: [ 4, 2, 3 ]
            // List: [ 1, 2, 3, 4 ]

            // Now we'll remove item 2.
            // Notice that while item 4 is at the front of the queue, it is also at the _end_ of the playlist.
            // Item 3 will have its playlist order updated, which may crash if the current item isn't updated to not reference beyond the end of the list.
            SetUserContext(user1Ctx);
            await Hub.RemovePlaylistItem(2);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(4, room.Queue.CurrentItem.ID);
            }
        }

        private async Task runGameplay()
        {
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            await Hub.StartMatch();

            await LoadAndFinishGameplay(ContextUser, ContextUser2);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            SetUserContext(ContextUser);
        }

        private async Task addItem()
        {
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });
        }

        private async Task checkCurrentItem(long expectedItemId)
        {
            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(expectedItemId, room.Settings.PlaylistItemId);
            }
        }

        private async Task checkOrder(params long[] itemIdsInOrder)
        {
            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(itemIdsInOrder, room.Playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).Select(item => item.ID));
            }
        }
    }
}
