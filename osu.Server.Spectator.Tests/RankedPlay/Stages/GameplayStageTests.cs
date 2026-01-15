// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class GameplayStageTests : RankedPlayStageImplementationTest
    {
        public GameplayStageTests()
            : base(RankedPlayStage.Gameplay)
        {
        }

        protected override async Task SetupForEnter()
        {
            await base.SetupForEnter();

            await Controller.ActivateCard(UserState.Hand.First());

            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser);
        }

        [Fact]
        public async Task DoesNotContinueToResultsOnCountdownFinish()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.Gameplay, RoomState.Stage);
        }

        [Fact]
        public async Task DoesNotContinueToResultsOnSinglePlayerCompletion()
        {
            await LoadGameplay(ContextUser, ContextUser2);
            await FinishGameplay(ContextUser);
            Assert.Equal(RankedPlayStage.Gameplay, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToResultsOnPlayerQuit()
        {
            await LoadGameplay(ContextUser, ContextUser2);
            await FinishGameplay(ContextUser);

            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Results, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToResultsOnGameplayCompletion()
        {
            await LoadAndFinishGameplay(ContextUser, ContextUser2);
            Assert.Equal(RankedPlayStage.Results, RoomState.Stage);
        }
    }
}
