// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class CountdownTest : MultiplayerTest
    {
        [Fact]
        public async Task StartAndStopCountdown()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
            countdown.TimeRemaining = TimeSpan.FromMinutes(1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown, _ => Task.CompletedTask);
                Assert.NotNull(usage.Item.FindCountdownById(countdown.ID));
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown)), Times.Once);

                await usage.Item!.StopCountdown(countdown);
                Assert.Null(usage.Item.FindCountdownById(countdown.ID));
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown.ID)), Times.Once);
            }
        }

        [Fact]
        public async Task CallbackInvokedWhenCountdownCompletes()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
            countdown.TimeRemaining = TimeSpan.FromSeconds(3);

            bool callbackInvoked = false;
            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown, _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                });

                task = usage.Item.GetCountdownTask(countdown);
            }

            await task;

            Assert.True(callbackInvoked);
        }

        [Fact]
        public async Task CallbackInvokedWhenCountdownSkippedToEnd()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
            countdown.TimeRemaining = TimeSpan.FromMinutes(1);

            bool callbackInvoked = false;
            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown, _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                });

                task = usage.Item.SkipToEndOfCountdown(countdown);
            }

            await task;

            Assert.True(callbackInvoked);
        }

        [Fact]
        public async Task CallbackNotInvokedWhenCountdownStopped()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown = Mock.Of<MultiplayerCountdown>();
            countdown.TimeRemaining = TimeSpan.FromMinutes(1);

            bool callbackInvoked = false;
            Task task;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown, _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                });

                task = usage.Item.GetCountdownTask(countdown);

                await usage.Item.StopCountdown(countdown);
            }

            await task;

            Assert.False(callbackInvoked);
        }

        [Fact]
        public async Task StartMultipleCountdowns()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
            MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown1, _ => Task.CompletedTask);
                await usage.Item!.StartCountdown(countdown2, _ => Task.CompletedTask);
                Assert.NotNull(usage.Item.FindCountdownById(countdown1.ID));
                Assert.NotNull(usage.Item.FindCountdownById(countdown2.ID));
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown2)), Times.Once);
                Receiver.Verify(r => r.MatchEvent(It.IsAny<CountdownStoppedEvent>()), Times.Never);
            }
        }

        [Fact]
        public async Task CallbackInvokedWhenMultipleCountdownsComplete()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
            MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
            countdown1.TimeRemaining = TimeSpan.FromSeconds(3);
            countdown2.TimeRemaining = TimeSpan.FromSeconds(3);

            bool callbackInvoked1 = false;
            bool callbackInvoked2 = false;
            Task task1;
            Task task2;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown1, _ =>
                {
                    callbackInvoked1 = true;
                    return Task.CompletedTask;
                });

                await usage.Item!.StartCountdown(countdown2, _ =>
                {
                    callbackInvoked2 = true;
                    return Task.CompletedTask;
                });

                task1 = usage.Item.GetCountdownTask(countdown1);
                task2 = usage.Item.GetCountdownTask(countdown2);
            }

            await task1;
            await task2;

            Assert.True(callbackInvoked1);
            Assert.True(callbackInvoked2);
        }

        [Fact]
        public async Task CallbackInvokedWhenMultipleCountdownsSkippedToEnd()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
            MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
            countdown1.TimeRemaining = TimeSpan.FromMinutes(1);
            countdown2.TimeRemaining = TimeSpan.FromMinutes(1);

            bool callbackInvoked1 = false;
            bool callbackInvoked2 = false;
            Task task1;
            Task task2;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown1, _ =>
                {
                    callbackInvoked1 = true;
                    return Task.CompletedTask;
                });

                await usage.Item!.StartCountdown(countdown2, _ =>
                {
                    callbackInvoked2 = true;
                    return Task.CompletedTask;
                });

                task1 = usage.Item.SkipToEndOfCountdown(countdown1);
                task2 = usage.Item.SkipToEndOfCountdown(countdown2);
            }

            await task1;
            await task2;

            Assert.True(callbackInvoked1);
            Assert.True(callbackInvoked2);
        }

        [Fact]
        public async Task CallbackNotInvokedWhenMultipleCountdownsStopped()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerCountdown countdown1 = Mock.Of<MultiplayerCountdown>();
            MultiplayerCountdown countdown2 = Mock.Of<MultiplayerCountdown>();
            countdown1.TimeRemaining = TimeSpan.FromMinutes(1);
            countdown2.TimeRemaining = TimeSpan.FromMinutes(1);

            bool callbackInvoked1 = false;
            bool callbackInvoked2 = false;
            Task task1;
            Task task2;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown1, _ =>
                {
                    callbackInvoked1 = true;
                    return Task.CompletedTask;
                });

                await usage.Item!.StartCountdown(countdown2, _ =>
                {
                    callbackInvoked2 = true;
                    return Task.CompletedTask;
                });

                task1 = usage.Item.SkipToEndOfCountdown(countdown1);
                task2 = usage.Item.SkipToEndOfCountdown(countdown2);

                // Only one countdown is stopped.
                await usage.Item.StopCountdown(countdown1);
            }

            await task1;
            await task2;

            Assert.False(callbackInvoked1);
            Assert.True(callbackInvoked2);
        }

        [Fact]
        public async Task StartingNewExclusiveCountdownReplacesExisting()
        {
            await Hub.JoinRoom(ROOM_ID);

            Mock<ExclusiveCountdown> countdown1 = new Mock<ExclusiveCountdown> { CallBase = true };
            Mock<MultiplayerCountdown> countdown2 = new Mock<MultiplayerCountdown> { CallBase = true };
            Mock<ExclusiveCountdown> countdown3 = new Mock<ExclusiveCountdown> { CallBase = true };

            countdown1.Setup(c => c.IsExclusive).Returns(true);
            countdown2.Setup(c => c.IsExclusive).Returns(false);
            countdown3.Setup(c => c.IsExclusive).Returns(true);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                await usage.Item!.StartCountdown(countdown1.Object, _ => Task.CompletedTask);
                await usage.Item!.StartCountdown(countdown2.Object, _ => Task.CompletedTask);
                await usage.Item!.StartCountdown(countdown3.Object, _ => Task.CompletedTask);
                Assert.Null(usage.Item.FindCountdownById(countdown1.Object.ID));
                Assert.NotNull(usage.Item.FindCountdownById(countdown2.Object.ID));
                Assert.NotNull(usage.Item.FindCountdownById(countdown3.Object.ID));
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown1.Object)), Times.Once);
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown2.Object)), Times.Once);
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStartedEvent>(e => e.Countdown == countdown3.Object)), Times.Once);
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown1.Object.ID)), Times.Once);
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown2.Object.ID)), Times.Never);
                Receiver.Verify(r => r.MatchEvent(It.Is<CountdownStoppedEvent>(e => e.ID == countdown3.Object.ID)), Times.Never);
            }
        }

        public class ExclusiveCountdown : MultiplayerCountdown
        {
            public override bool IsExclusive => true;
        }
    }
}
