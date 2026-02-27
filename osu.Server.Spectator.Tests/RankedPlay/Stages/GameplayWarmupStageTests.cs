// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
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
        }

        [Fact]
        public async Task DoesNotContinueToGameplayWithoutReady()
        {
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            SetUserContext(ContextUser2);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            SetUserContext(ContextUser);

            for (int i = 0; i < 5; i++)
            {
                await FinishCountdown();
                Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);
            }
        }

        [Fact]
        public async Task DoesNotContinueToGameplayWithoutBeatmapAvailable()
        {
            await Hub.ChangeState(MultiplayerUserState.Ready);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            SetUserContext(ContextUser);

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);
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
    }
}
