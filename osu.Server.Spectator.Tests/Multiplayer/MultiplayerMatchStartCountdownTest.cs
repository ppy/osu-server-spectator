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
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerMatchStartCountdownTest : MultiplayerTest
    {
        private const int test_timeout = 60000;

        [Fact(Timeout = test_timeout)]
        public async Task CanStartCountdownIfNotReady()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.FindCountdownOfType<MatchStartCountdown>());
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStartedEvent>()), Times.Once);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task GameplayStartsWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromSeconds(3) });

            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                MultiplayerCountdown? countdown = room.FindCountdownOfType<MatchStartCountdown>();
                Assert.NotNull(countdown);

                task = room.GetCountdownTask(countdown);
            }

            await task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
                Receiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task GameplayStartsWhenCountdownFinished()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                MultiplayerCountdown? countdown = room.FindCountdownOfType<MatchStartCountdown>();
                Assert.NotNull(countdown);
                Assert.InRange(countdown.TimeRemaining.TotalSeconds, 30, 60);
                Receiver.Verify(r => r.LoadRequested(), Times.Never);
            }

            await skipToEndOfCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
                Receiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task GameplayDoesNotStartWhenCountdownCancelled()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            int countdownId;
            using (var usage = await Hub.GetRoom(ROOM_ID))
                countdownId = usage.Item!.FindCountdownOfType<MatchStartCountdown>()!.ID;

            await Hub.SendMatchRequest(new StopCountdownRequest(countdownId));
            await skipToEndOfCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStoppedEvent>()), Times.Exactly(1));
                Receiver.Verify(r => r.LoadRequested(), Times.Never);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task NewCountdownOverridesExisting()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            // Start first countdown.

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.NotNull(room.FindCountdownOfType<MatchStartCountdown>());
            }

            Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStartedEvent>()), Times.Once);

            // Start second countdown.

            MultiplayerCountdown firstCountdown;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                firstCountdown = room.FindCountdownOfType<MatchStartCountdown>()!;
                Assert.NotNull(firstCountdown);
            }

            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            MultiplayerCountdown secondCountdown;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                secondCountdown = room.FindCountdownOfType<MatchStartCountdown>()!;
                Assert.NotNull(secondCountdown);
                Assert.NotEqual(firstCountdown, secondCountdown);

                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStartedEvent>()), Times.Exactly(2));
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == firstCountdown.ID)), Times.Once);
                Receiver.Verify(r => r.LoadRequested(), Times.Never);
            }

            await skipToEndOfCountdown();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == secondCountdown.ID)), Times.Once);
                Receiver.Verify(r => r.LoadRequested(), Times.Once);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task CanNotStartCountdownDuringMatch()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) }));
        }

        [Fact(Timeout = test_timeout)]
        public async Task TimeRemainingUpdatedOnJoin()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                MultiplayerCountdown? countdown = usage.Item!.FindCountdownOfType<MatchStartCountdown>();
                Assert.NotNull(countdown);
                Assert.Equal(60, countdown.TimeRemaining.TotalSeconds);
            }

            Thread.Sleep(2000);

            SetUserContext(ContextUser2);
            var secondRoom = await Hub.JoinRoom(ROOM_ID);

            Assert.True(secondRoom.ActiveCountdowns.OfType<MatchStartCountdown>().Single().TimeRemaining.TotalSeconds < 60);
        }

        [Fact(Timeout = test_timeout)]
        public async Task CanNotStartCountdownIfAutoStartEnabled()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) }));
        }

        [Fact(Timeout = test_timeout)]
        public async Task AutoStartCountdownDoesNotStartWithZeroDuration()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task AutoStartCountdownStartsWhenHostReadies()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            await skipToEndOfCountdown();
            Receiver.Verify(r => r.LoadRequested(), Times.Once);
        }

        [Fact(Timeout = test_timeout)]
        public async Task AutoStartCountdownStartsWhenGuestReadies()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            await skipToEndOfCountdown();
            Receiver.Verify(r => r.LoadRequested(), Times.Once);
        }

        [Fact(Timeout = test_timeout)]
        public async Task AutoStartCountdownContinuesWhileAllUsersNotReady()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            // The countdown should continue after the guest user unreadies (the only ready user).
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.FindCountdownOfType<MatchStartCountdown>());
            }

            await skipToEndOfCountdown();

            // When the countdown ends, it should not trigger LoadRequested(), but the current item in the queue should be skipped.
            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist.Where(p => p.Expired));
                Receiver.Verify(r => r.LoadRequested(), Times.Never);
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task AutoStartCountdownCanNotBeCancelled()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });
            await Hub.ChangeState(MultiplayerUserState.Ready);

            int countdownId;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                MultiplayerCountdown? countdown = usage.Item!.FindCountdownOfType<MatchStartCountdown>();

                Assert.NotNull(countdown);

                countdownId = countdown.ID;
            }

            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new StopCountdownRequest(countdownId)));

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.NotNull(room.FindCountdownOfType<MatchStartCountdown>());
            }
        }

        [Fact(Timeout = test_timeout)]
        public async Task CountdownStoppedWhenAutoStartDurationChanged()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.SendMatchRequest(new StartMatchCountdownRequest { Duration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.FromMinutes(1) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
            }

            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.NotNull(usage.Item!.FindCountdownOfType<MatchStartCountdown>());

            await Hub.ChangeSettings(new MultiplayerRoomSettings { AutoStartDuration = TimeSpan.Zero });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.FindCountdownOfType<MatchStartCountdown>());
            }
        }

        private async Task skipToEndOfCountdown()
        {
            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                task = room.SkipToEndOfCountdown(room.FindCountdownOfType<MatchStartCountdown>());
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
