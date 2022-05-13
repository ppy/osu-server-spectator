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
        public async Task RoundRobinOrderingWithGameplay()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

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
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

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

        private async Task runGameplay()
        {
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);
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
