// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages;
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
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;
            ((ResultsStage)MatchController.Stage).ScoreRetrievalWaitTime = TimeSpan.FromSeconds(1);

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 }
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(500_000, User2State.Life);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            }, UserState.DamageInfo);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 500_000,
                Damage = 500_000,
                OldLife = 1_000_000,
                NewLife = 500_000,
                DirectDamage = 500_000
            }, User2State.DamageInfo);
        }

        [Fact]
        public async Task DamageTakenWithLateArrivingScore()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 }
                    ]));

            Task enterTask = MatchController.Stage.Enter();

            await Task.Delay(1000);

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            await enterTask;

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(750_000, User2State.Life);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            }, UserState.DamageInfo);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 250_000,
                Damage = 250_000,
                OldLife = 1_000_000,
                NewLife = 750_000,
                DirectDamage = 250_000
            }, User2State.DamageInfo);
        }

        [Fact]
        public async Task DamageTakenIsDifferenceBetweenScores()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(750_000, User2State.Life);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            }, UserState.DamageInfo);

            Assert.Equal(new RankedPlayDamageInfo
            {
                RawDamage = 250_000,
                Damage = 250_000,
                OldLife = 1_000_000,
                NewLife = 750_000,
                DirectDamage = 250_000
            }, User2State.DamageInfo);
        }

        [Fact]
        public async Task RoomDamageMultiplierAdded()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            RoomState.DamageMultiplier = 1.5;

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(500_000, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 250_000,
                Damage = 500_000,
                OldLife = 1_000_000,
                NewLife = 500_000,
                DirectDamage = 250_000,
                Multiplier = 2
            });
        }

        [Fact]
        public async Task UserMultiplierAdded()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            UserState.DamageMultiplier = 1.5;

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(500_000, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 250_000,
                Damage = 500_000,
                OldLife = 1_000_000,
                NewLife = 500_000,
                DirectDamage = 250_000,
                Multiplier = 2
            });
        }

        [Fact]
        public async Task CombinedMultipliers()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 490_000 },
                    ]));

            RoomState.DamageMultiplier = 2;
            UserState.DamageMultiplier = 3;

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(950_000, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 10_000,
                Damage = 50_000,
                OldLife = 1_000_000,
                NewLife = 950_000,
                DirectDamage = 10_000,
                Multiplier = 5
            });
        }

        [Fact]
        public async Task FatalHitFromMaxHpActivatesLastStand()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 1_000_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 0 },
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(1, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 1_000_000,
                Damage = 1_000_000,
                OldLife = 1_000_000,
                NewLife = 1,
                DirectDamage = 1_000_000
            });

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(0, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 1_000_000,
                Damage = 1_000_000,
                OldLife = 1,
                NewLife = 0,
                DirectDamage = 1_000_000
            });
        }

        [Fact]
        public async Task FatalHitFromBelowMaxHpDoesNotActivateLastStand()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 999_999;

            ((ResultsStage)MatchController.Stage).BaseDamage = 0;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 1_000_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 0 },
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(0, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 1_000_000,
                Damage = 1_000_000,
                OldLife = 999_999,
                NewLife = 0,
                DirectDamage = 1_000_000
            });
        }

        [Fact]
        public async Task BonusDamageSource()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            RoomState.DamageMultiplier = 1.5;

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(450_000, User2State.Life);

            Assert.Equal(UserState.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 0,
                Damage = 0,
                OldLife = 1_000_000,
                NewLife = 1_000_000,
            });

            Assert.Equal(User2State.DamageInfo, new RankedPlayDamageInfo
            {
                RawDamage = 300_000,
                Damage = 550_000,
                OldLife = 1_000_000,
                NewLife = 450_000,
                DirectDamage = 250_000,
                Multiplier = 2,
                BonusDamage = 50_000
            });
        }

        [Fact]
        public async Task NoDamageOnTie()
        {
            UserState.Life = 1_000_000;
            User2State.Life = 1_000_000;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 500_000 },
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(1_000_000, User2State.Life);
        }

        [Fact]
        public async Task MultipliersIncrease()
        {
            // Definite winner.
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 250_000 },
                    ]));

            await MatchController.Stage.Enter();
            await FinishCountdown();

            Assert.Equal(1, RoomState.DamageMultiplier);
            Assert.Equal(1, UserState.DamageMultiplier);
            Assert.Equal(0.5, User2State.DamageMultiplier);

            // Tie.
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 500_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 500_000 },
                    ]));

            await MatchController.GotoStage(RankedPlayStage.Results);
            await FinishCountdown();

            Assert.Equal(1.5, RoomState.DamageMultiplier);
            Assert.Equal(1, UserState.DamageMultiplier);
            Assert.Equal(0.5, User2State.DamageMultiplier);
        }

        [Fact]
        public async Task TotalScoreWithoutModsUsed()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore
                        {
                            user_id = USER_ID,
                            total_score = 1_000_000,
                            ScoreData = { TotalScoreWithoutMods = 500_000 }
                        },
                        new SoloScore
                        {
                            user_id = USER_ID_2,
                            total_score = 0
                        },
                    ]));

            await MatchController.Stage.Enter();
            await FinishCountdown();

            Assert.Equal(1_000_000, UserState.Life);
            Assert.Equal(450_000, User2State.Life);
        }

        [Fact]
        public async Task UserNotKilledIfQuitInFinalRound()
        {
            User2State.Life = 1_000_000;
            UserState.Life = 0;

            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            Assert.Equal(1_000_000, User2State.Life);
        }

        [Fact]
        public async Task RoundsWonIncrementedForWinner()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 1 },
                        new SoloScore { user_id = USER_ID_2, total_score = 0 }
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(1, UserState.RoundsWon);
            Assert.Equal(0, User2State.RoundsWon);
        }

        [Fact]
        public async Task RoundsWonNotIncrementedOnTie()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 1 },
                        new SoloScore { user_id = USER_ID_2, total_score = 1 }
                    ]));

            await MatchController.Stage.Enter();

            Assert.Equal(0, UserState.RoundsWon);
            Assert.Equal(0, User2State.RoundsWon);
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

        [Fact]
        public async Task ContinuesToEndedWhenAnyPlayerLeaves()
        {
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Results, RoomState.Stage);
            Assert.Equal(0, UserState.Life);
        }

        [Fact]
        public void UserRatingNotUpdatedWithRoundsRemaining()
        {
            Assert.False(MatchController.UserRatingsUpdated);
        }

        [Fact]
        public async Task UserRatingUpdatedImmediatelyInFinalRound()
        {
            UserState.Life = 0;
            await MatchController.Stage.Enter();

            Assert.True(MatchController.UserRatingsUpdated);
        }

        [Fact]
        public async Task UserRatingUpdatedImmediatelyOnPlayerLeave()
        {
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Results, RoomState.Stage);
            Assert.True(MatchController.UserRatingsUpdated);
        }
    }
}
