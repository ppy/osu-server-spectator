// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class ResultsStage : RankedPlayStageImplementation
    {
        /// <summary>
        /// Amount of time to wait for scores to arrive in the database before continuing.
        /// </summary>
        public TimeSpan ScoreRetrievalWaitTime { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Base amount of damage taken per round.
        /// </summary>
        public int BaseDamage { get; set; } = 50_000;

        public ResultsStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.Results;
        protected override TimeSpan Duration => TimeSpan.FromSeconds(15);

        private int? winningUserId;

        protected override async Task Begin()
        {
            // Collect all scores from the database.
            List<SoloScore> scores = [];

            using (var db = DbFactory.GetInstance())
            {
                // Wait up to 10 seconds to retrieve scores for all players, before continuing and giving them 0 score.
                using (var cts = new CancellationTokenSource(ScoreRetrievalWaitTime))
                {
                    SoloScore[] retrievedScores = [];

                    while (!cts.IsCancellationRequested)
                    {
                        retrievedScores = (await db.GetAllScoresForPlaylistItem(Room.Settings.PlaylistItemId)).ToArray();

                        if (retrievedScores.Length == State.Users.Count)
                            break;

                        await Task.Delay(1000, CancellationToken.None);
                    }

                    scores.AddRange(retrievedScores);
                }
            }

            foreach ((int userId, RankedPlayUserInfo info) in State.Users)
            {
                // Add dummy scores for all users that did not play the map.
                if (scores.All(s => s.user_id != userId))
                    scores.Add(new SoloScore { user_id = (uint)userId });

                // Populate the models with a default damage info.
                info.DamageInfo = Controller.Damage(userId);
            }

            int winningTotalScore = (int)scores.Select(s => s.total_score).Max();
            SoloScore[] winningScores = scores.Where(u => u.total_score == winningTotalScore).ToArray();
            winningUserId = winningScores.Length == 1 ? (int)winningScores.Single().user_id : null;

            if (winningUserId != null)
            {
                // Winner: losing player takes damage.
                SoloScore losingScore = scores.Single(u => u.user_id != winningUserId);

                int attackDamage = winningTotalScore - (int)losingScore.total_score;
                double attackMultiplier = State.DamageMultiplier + State.Users[winningUserId.Value].DamageMultiplier;

                State.Users[(int)losingScore.user_id].DamageInfo = Controller.Damage((int)losingScore.user_id, attackDamage, attackMultiplier, BaseDamage);
            }
            else
            {
                // Tie: both players take the base amount of damage.
                foreach ((int userId, RankedPlayUserInfo info) in State.Users)
                    info.DamageInfo = Controller.Damage(userId, bonusDamage: BaseDamage);
            }

            if (Controller.Ranked)
            {
                await Controller.MatchmakingService.RecordBeatmapResult(
                    Controller.Pool.id,
                    Room.CurrentPlaylistItem.BeatmapID,
                    Room.CurrentPlaylistItem.RequiredMods.ToArray(),
                    scores.Select(s => (int)s.total_score).ToArray(),
                    scores.Select(s => Controller.RatingByUser[(int)s.user_id]).ToArray());
            }

            if (!HasGameplayRoundsRemaining())
                await Controller.HandleMatchCompleted();
        }

        protected override async Task Finish()
        {
            // Award the winning player with their own multiplier boost.
            if (winningUserId != null)
                State.Users[winningUserId.Value].DamageMultiplier += 0.5;

            foreach ((_, RankedPlayUserInfo userInfo) in State.Users)
                userInfo.DamageInfo = null;

            if (HasGameplayRoundsRemaining())
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
            else
                await Controller.GotoStage(RankedPlayStage.Ended);
        }

        public override async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // Allow players to leave early without incurring a loss if they know gameplay won't continue.
            if (HasGameplayRoundsRemaining())
                await KillUser(user);

            // Remain in the results stage, which will naturally transition to the ended stage once the countdown expires.
        }
    }
}
