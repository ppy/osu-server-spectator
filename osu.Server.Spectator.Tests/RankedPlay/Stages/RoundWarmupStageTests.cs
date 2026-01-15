// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class RoundWarmupStageTests : RankedPlayStageImplementationTest
    {
        public RoundWarmupStageTests()
            : base(RankedPlayStage.RoundWarmup)
        {
        }

        [Fact]
        public async Task CurrentRoundIncrementedOnEnter()
        {
            Assert.Equal(1, RoomState.CurrentRound);

            for (int i = 2; i < 5; i++)
            {
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
                Assert.Equal(i, RoomState.CurrentRound);
            }
        }

        [Fact]
        public async Task NewActiveUserOnEnter()
        {
            int activeUserId = RoomState.ActiveUserId;
            Assert.Contains([activeUserId], [USER_ID, USER_ID_2]);

            for (int i = 0; i < 5; i++)
            {
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
                Assert.NotEqual(activeUserId, RoomState.ActiveUserId);
                activeUserId = RoomState.ActiveUserId;
            }
        }

        [Fact]
        public async Task ContinuesToDiscard()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardDiscard, RoomState.Stage);
        }
    }
}
