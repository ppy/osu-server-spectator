// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class ResultsStage : RankedPlayStageImplementation
    {
        public ResultsStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.Results;
        protected override TimeSpan Duration => TimeSpan.FromSeconds(10);

        private bool anyPlayerDefeated;

        protected override async Task Begin()
        {
            // Collect all scores from the database.
            List<SoloScore> scores = [];
            using (var db = DbFactory.GetInstance())
                scores.AddRange(await db.GetAllScoresForPlaylistItem(Room.Settings.PlaylistItemId));

            // Add dummy scores for all users that did not play the map.
            foreach ((int userId, _) in State.Users)
            {
                if (scores.All(s => s.user_id != userId))
                    scores.Add(new SoloScore { user_id = (uint)userId });
            }

            // If all players have 0 resulting score, each shall take 1 point of damage (before multipliers).
            int maxTotalScore = (int)Math.Max(1, scores.Select(s => s.total_score).Max());

            foreach (var score in scores)
            {
                double damage = maxTotalScore - (int)score.total_score;
                damage *= State.DamageMultiplier;
                damage = Math.Ceiling(damage);

                var userInfo = State.Users[(int)score.user_id];
                userInfo.Life = Math.Max(0, userInfo.Life - (int)damage);

                anyPlayerDefeated |= userInfo.Life == 0;
            }
        }

        protected override async Task Finish()
        {
            // Todo: This only works for 2 players. This will need to be adjusted if we ever have more.
            if (anyPlayerDefeated)
                await Controller.GotoStage(RankedPlayStage.Ended);
            else
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
        }
    }
}
