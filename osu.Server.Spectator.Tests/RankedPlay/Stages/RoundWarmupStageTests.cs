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
                await MatchController.GotoStage(RankedPlayStage.RoundWarmup);
                Assert.Equal(i, RoomState.CurrentRound);
            }
        }

        [Fact]
        public async Task NewActiveUserOnEnter()
        {
            int activeUserId = RoomState.ActiveUserId!.Value;
            Assert.Contains([activeUserId], [USER_ID, USER_ID_2]);

            for (int i = 0; i < 5; i++)
            {
                await MatchController.GotoStage(RankedPlayStage.RoundWarmup);
                Assert.NotEqual(activeUserId, RoomState.ActiveUserId);
                activeUserId = RoomState.ActiveUserId!.Value;
            }
        }

        [Fact]
        public async Task ContinuesToDiscard()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardDiscard, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToEndedWhenAnyPlayerLeaves()
        {
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
            Assert.Equal(0, UserState.Life);
        }

        [Fact]
        public async Task RoundMultiplierAdjustment()
        {
            double[] expectedMultipliers = [1, 1, 2, 2.5, 3, 3.5, 4, 4.5, 5];

            for (int i = 0; i < expectedMultipliers.Length; i++)
            {
                Assert.Equal(expectedMultipliers[i], RoomState.DamageMultiplier);

                // Go to the next round, for the next iteration.
                await MatchController.GotoStage(RankedPlayStage.RoundWarmup);
            }
        }

        [Fact]
        public async Task CardDrawnOnNextPlayerRound()
        {
            // First round for each player doesn't draw any cards.
            int[] expectedCardCounts = [5, 5, 6, 6, 7, 7];

            for (int i = 0; i < expectedCardCounts.Length; i++)
            {
                Assert.Equal(expectedCardCounts[i], RoomState.Users[RoomState.ActiveUserId!.Value].Hand.Count);

                // Go to the next round, for the next iteration.
                await MatchController.GotoStage(RankedPlayStage.RoundWarmup);
            }
        }
    }
}
