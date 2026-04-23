// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class GameplayWarmupStageTests : RankedPlayStageImplementationTest
    {
        public GameplayWarmupStageTests()
            : base(RankedPlayStage.GameplayWarmup)
        {
        }

        protected override async Task SetupForEnter()
        {
            await base.SetupForEnter();

            await MatchController.ActivateCard(RoomState.ActiveUser!.Hand.First());
        }

        [Fact]
        public async Task ContinuesToGameplayWhenAllPlayersReady()
        {
            await MarkCurrentUserReadyAndAvailable();
            Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);

            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser);

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.Gameplay, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToEndedWhenAnyPlayerLeaves()
        {
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
            Assert.Equal(0, UserState.Life);
        }

        [Fact]
        public async Task ContinuesToNextRoundWhenAnyPlayerFailsToBecomeReady()
        {
            int firstActiveUser = RoomState.ActiveUserId!.Value;

            await MarkCurrentUserReadyAndAvailable();
            Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);

            int secondActiveUser = RoomState.ActiveUserId!.Value;

            Assert.NotEqual(firstActiveUser, secondActiveUser);

            Assert.Equal(4, RoomState.Users[firstActiveUser].Hand.Count);
            Assert.Equal(5, RoomState.Users[secondActiveUser].Hand.Count);

            Assert.Equal(1_000_000, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }

        [Fact]
        public async Task ContinuesToNextRoundWhenAllPlayersFailToBecomeReady()
        {
            int firstActiveUser = RoomState.ActiveUserId!.Value;

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);

            int secondActiveUser = RoomState.ActiveUserId!.Value;

            Assert.NotEqual(firstActiveUser, secondActiveUser);

            Assert.Equal(4, RoomState.Users[firstActiveUser].Hand.Count);
            Assert.Equal(5, RoomState.Users[secondActiveUser].Hand.Count);

            Assert.Equal(900_000, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }

        [Fact]
        public async Task ContinuesToEndedWhenPlayerDiesFromFailingToBecomeReady()
        {
            RoomState.Users[USER_ID].Life = 50_000;

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);

            Assert.Equal(0, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }
    }
}
