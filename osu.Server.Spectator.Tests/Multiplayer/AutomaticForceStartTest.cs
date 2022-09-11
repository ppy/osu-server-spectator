// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
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

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.IsType<ForceGameplayStartCountdown>(usage.Item?.Countdown);
        }

        [Fact]
        public async Task CountdownStopsWhenAllPlayersAbort()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.StartMatch();
            await Hub.AbortGameplay();

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.Null(usage.Item?.Countdown);
        }

        [Fact]
        public async Task LoadingUsersAbortWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            await skipToEndOfCountdown();

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

            await skipToEndOfCountdown();

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

            await skipToEndOfCountdown();

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

        private async Task skipToEndOfCountdown()
        {
            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                task = room.SkipToEndOfCountdown();
            }

            try
            {
                await task;
            }
            catch (TaskCanceledException)
            {
                // don't care if task was cancelled.
            }
        }
    }
}
