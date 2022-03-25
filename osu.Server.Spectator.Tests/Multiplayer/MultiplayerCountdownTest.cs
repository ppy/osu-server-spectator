// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerCountdownTest : MultiplayerTest
    {
        [Fact]
        public async Task CanStartCountdownIfNotReady()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.Countdown);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownChangedEvent>()), Times.Once);
            }
        }

        [Fact]
        public async Task GameplayStartsWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromSeconds(3) });
            await waitForCountingDown();

            int attempts = 1000;

            while (attempts-- > 0)
            {
                using (var usage = await Hub.GetRoom(ROOM_ID))
                {
                    var room = usage.Item;
                    Debug.Assert(room != null);

                    if (!room.IsCountdownRunning)
                        break;

                    Thread.Sleep(10);
                }
            }

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Countdown);
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact]
        public async Task GameplayStartsWhenCountdownFinished()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });
            await waitForCountingDown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.Countdown);
                Assert.InRange(room.Countdown!.TimeRemaining.TotalSeconds, 30, 60);
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Never);
            }

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Countdown);
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact]
        public async Task GameplayDoesNotStartWhenCountdownCancelled()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });
            await Hub.SendMatchRequest(new StopCountdownRequest());

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Countdown);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownChangedEvent>()), Times.Exactly(2));
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Never);
            }
        }

        [Fact]
        public async Task NewCountdownOverridesExisting()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            // Start first countdown.

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });
            await waitForCountingDown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.Countdown);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownChangedEvent>()), Times.Once);
            }

            // Start second countdown.

            MultiplayerCountdown? existingCountdown;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                existingCountdown = room.Countdown;
            }

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            // Wait for the second countdown to begin running.
            int attempts = 200;

            while (attempts-- > 0)
            {
                using (var usage = await Hub.GetRoom(ROOM_ID))
                {
                    var room = usage.Item;
                    Debug.Assert(room != null);

                    if (room.Countdown != null && room.Countdown != existingCountdown)
                        break;
                }

                Thread.Sleep(10);
            }

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.Countdown);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownChangedEvent>()), Times.Exactly(3));
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Never);
            }

            await finishCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Countdown);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownChangedEvent>()), Times.Exactly(4));
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact]
        public async Task CanNotStartCountdownDuringMatch()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) }));
        }

        [Fact]
        public async Task TimeRemainingUpdatedOnJoin()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });
            await waitForCountingDown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.Countdown?.TimeRemaining.TotalSeconds == 60);
            }

            Thread.Sleep(2000);

            SetUserContext(ContextUser2);
            var secondRoom = await Hub.JoinRoom(ROOM_ID);

            Assert.True(secondRoom.Countdown?.TimeRemaining.TotalSeconds < 60);
        }

        [Fact]
        public async Task AutoStartCountdownDoesNotStartWithZeroDuration()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.False(room.IsCountdownRunning);
            }
        }

        [Fact]
        public async Task AutoStartCountdownStartsWhenHostReadies()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await waitForCountingDown();

            await finishCountdown();
            GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
        }

        [Fact]
        public async Task AutoStartCountdownStartsWhenGuestReadies()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await waitForCountingDown();

            await finishCountdown();
            GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
        }

        [Fact]
        public async Task AutoStartCountdownContinuesWhileAllUsersNotReady()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await waitForCountingDown();

            // The countdown should continue after the guest user unreadies (the only ready user).
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.False(room.CountdownCancellationRequested);
                Assert.True(room.IsCountdownRunning);
            }

            await finishCountdown();

            // When the countdown ends, it should not trigger LoadRequested(), but the current item in the queue should be skipped.
            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist.Where(p => p.Expired));
                GameplayReceiver.Verify(r => r.LoadRequested(), Times.Never);
            }
        }

        [Fact]
        public async Task AutoStartCountdownCanNotBeCancelled()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await waitForCountingDown();

            await Hub.SendMatchRequest(new StopCountdownRequest());

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.False(room.CountdownCancellationRequested);
                Assert.True(room.IsCountdownRunning);
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
        }
    }
}
