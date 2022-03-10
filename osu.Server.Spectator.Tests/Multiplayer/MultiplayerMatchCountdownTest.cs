// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerMatchCountdownTest : MultiplayerTest
    {
        [Fact]
        public async Task CannotStartCountdownIfNotReady()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.SendMatchRequest(new MatchStartCountdownRequest { Delay = TimeSpan.FromMinutes(1) });
            Receiver.Verify(r => r.MatchEvent(It.IsAny<MatchStartCountdownEvent>()), Times.Never);
        }

        [Fact]
        public async Task GameplayStartedWhenCountdownEnds()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            await Hub.SendMatchRequest(new MatchStartCountdownRequest { Delay = TimeSpan.FromMinutes(1) });
            Receiver.Verify(r => r.MatchEvent(It.Is<MatchStartCountdownEvent>(e => (e.EndTime - DateTimeOffset.Now).Seconds > 30)), Times.Once);

            await finishCountdown();
            GameplayReceiver.Verify(r => r.MatchStarted(), Times.Once);
        }

        private async Task finishCountdown()
        {
            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                await room.FinishCountdown();
            }
        }
    }
}
