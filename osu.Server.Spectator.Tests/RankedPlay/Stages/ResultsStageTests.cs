// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class ResultsStageTests : RankedPlayStageImplementationTest
    {
        public ResultsStageTests()
            : base(RankedPlayStage.Results)
        {
        }

        [Fact]
        public async Task DamageTakenWithMissingScore()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 }
                    ]));

            await Controller.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(500_000, User2State.Life);
        }

        [Fact]
        public async Task DamageTakenIsDifferenceBetweenScores()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            await Controller.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(750_000, User2State.Life);
        }

        [Fact]
        public async Task DamageMultiplierAdded()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            RoomState.DamageMultiplier = 2;

            await Controller.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(500_000, User2State.Life);
        }

        [Fact]
        public async Task UserNotKilledIfQuitInFinalRound()
        {
            UserState.Life = 0;

            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            Assert.Equal(1_000_000, User2State.Life);
        }

        [Fact]
        public async Task ContinuesToRoundWarmupAndCardPlayNotInFinalRound()
        {
            await FinishCountdown();

            Assert.Equal(2, RoomState.CurrentRound);
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToEndedInFinalRound()
        {
            UserState.Life = 0;

            await FinishCountdown();

            Assert.Equal(1, RoomState.CurrentRound);
            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
        }
    }
}
