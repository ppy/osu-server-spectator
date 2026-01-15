// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class FinishCardPlayStageTests : RankedPlayStageImplementationTest
    {
        public FinishCardPlayStageTests()
            : base(RankedPlayStage.FinishCardPlay)
        {
        }

        protected override async Task SetupForEnter()
        {
            await base.SetupForEnter();

            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser);
        }

        [Fact]
        public void UsersUnreadiedOnEnter()
        {
            Assert.Equal(MultiplayerUserState.Idle, Room.Users[0].State);
            Assert.Equal(BeatmapAvailability.Unknown().State, Room.Users[0].BeatmapAvailability.State);
        }

        [Fact]
        public async Task DoesNotContinueToGameplayWithoutBeatmapAvailable()
        {
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            for (int i = 0; i < 5; i++)
            {
                await FinishCountdown();
                Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);
            }
        }

        [Fact]
        public async Task ContinuesToGameplayWarmupWhenAllPlayersReady()
        {
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);
        }
    }
}
