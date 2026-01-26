// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class EndedStageTests : RankedPlayStageImplementationTest
    {
        public EndedStageTests()
            : base(RankedPlayStage.Ended)
        {
        }

        protected override async Task SetupForEnter()
        {
            await base.SetupForEnter();

            UserState.Rating = 1500;
            User2State.Rating = 1500;
        }

        [Fact]
        public void DrawWithAllUsersAtMaxLife()
        {
            Assert.Null(RoomState.WinningUserId);
            Assert.Equal(UserState.Rating, UserState.RatingAfter);
            Assert.Equal(User2State.Rating, User2State.RatingAfter);
        }

        [Fact]
        public async Task WinnerIsUserWithMaxLife()
        {
            User2State.Life = 500_000;
            await MatchController.Stage.Enter();

            Assert.Equal(USER_ID, RoomState.WinningUserId);
            Assert.True(UserState.RatingAfter > UserState.Rating);
            Assert.True(User2State.RatingAfter < User2State.Rating);

            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(
                It.Is<matchmaking_user_stats>(stats => stats.user_id == USER_ID && (int)Math.Round(stats.EloData.Rating.Mu) == UserState.RatingAfter && stats.EloData.ContestCount == 1)
            ), Times.Exactly(1));

            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(
                It.Is<matchmaking_user_stats>(stats => stats.user_id == USER_ID_2 && (int)Math.Round(stats.EloData.Rating.Mu) == User2State.RatingAfter && stats.EloData.ContestCount == 1)
            ), Times.Exactly(1));
        }

        [Fact]
        public async Task EloDoesNotUpdateOnAbortedMatch()
        {
            Database.Invocations.Clear();

            RoomState.CurrentRound = 0;
            await MatchController.Stage.Enter();

            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(It.IsAny<matchmaking_user_stats>()), Times.Never);
        }
    }
}
