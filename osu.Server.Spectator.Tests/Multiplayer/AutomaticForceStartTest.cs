// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class AutomaticForceStartTest : MultiplayerTest
    {
        [Fact]
        public async Task CountdownStartsWhenMatchStarts()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.StartMatch();
            await waitForCountingDown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.True(usage.Item?.IsCountdownRunning);
        }

        [Fact]
        public async Task CountdownStopsWhenAllPlayersAbort()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.StartMatch();
            await waitForCountingDown();

            await Hub.AbortGameplay();

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.True(usage.Item?.IsCountdownStoppedOrCancelled);
        }

        [Fact]
        public async Task LoadingUsersAbortWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.State == MultiplayerRoomState.Open);
                Assert.Equal(MultiplayerUserState.Idle, room.Users.Single(u => u.UserID == USER_ID).State);

                UserReceiver.Verify(r => r.LoadAborted(), Times.Once);
                UserReceiver.Verify(r => r.GameplayStarted(), Times.Never);
            }
        }

        [Fact]
        public async Task LoadedUsersStartWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.State == MultiplayerRoomState.Playing);
                Assert.Equal(MultiplayerUserState.Playing, room.Users.Single(u => u.UserID == USER_ID).State);

                UserReceiver.Verify(r => r.LoadAborted(), Times.Never);
                UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
            }
        }

        [Fact]
        public async Task ReadyAndLoadedUsersStartWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            // User 1 becomes ready for gameplay.
            SetUserContext(ContextUser);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            // User 2 becomes loaded.
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.State == MultiplayerRoomState.Playing);
                Assert.Equal(MultiplayerUserState.Playing, room.Users.Single(u => u.UserID == USER_ID).State);
                Assert.Equal(MultiplayerUserState.Playing, room.Users.Single(u => u.UserID == USER_ID_2).State);

                UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
                User2Receiver.Verify(r => r.GameplayStarted(), Times.Once);
            }
        }

        private async Task finishCountdown()
        {
            ServerMultiplayerRoom? room;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                room = usage.Item;
                room?.SkipToEndOfCountdown();
            }

            Debug.Assert(room != null);

            int attempts = 200;
            while (attempts-- > 0 && room.IsCountdownRunning)
                Thread.Sleep(10);
        }

        private async Task waitForCountingDown()
        {
            ServerMultiplayerRoom? room;

            using (var usage = await Hub.GetRoom(ROOM_ID))
                room = usage.Item;

            Debug.Assert(room != null);

            int attempts = 200;
            while (attempts-- > 0 && room.Countdown == null)
                Thread.Sleep(10);

            Assert.NotNull(room.Countdown);
        }
    }
}
