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

            await MatchController.ActivateCard(UserState.Hand.First());

            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser);
        }

        [Fact]
        public async Task ContinuesToGameplay()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.Gameplay, RoomState.Stage);
        }
    }
}
