// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class EndedStage : RankedPlayStageImplementation
    {
        public EndedStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.Ended;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            // Check if the match has started.
            if (State.CurrentRound == 0)
                return;

            using (var db = DbFactory.GetInstance())
            {
                PlackettLuce model = new PlackettLuce
                {
                    Mu = 1500,
                    Sigma = 350,
                    Beta = 175,
                    Tau = 3.5
                };

                List<matchmaking_user_stats> stats = [];
                List<ITeam> teams = [];
                List<double> ranks = [];
                int rankIndex = -1;

                foreach ((int userId, _) in State.Users.OrderByDescending(u => u.Value.Life))
                {
                    matchmaking_user_stats userStats = await db.GetMatchmakingUserStatsAsync(userId, Controller.PoolId) ?? new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        pool_id = Controller.PoolId
                    };

                    stats.Add(userStats);
                    teams.Add(new Team { Players = [model.Rating(userStats.EloData.Rating.Mu, userStats.EloData.Rating.Sig)] });
                    ranks.Add(++rankIndex);
                }

                ITeam[] newRatings = model.Rate(teams, ranks).ToArray();

                for (int i = 0; i < stats.Count; i++)
                {
                    stats[i].EloData.ContestCount++;
                    stats[i].EloData.Rating = new EloRating(newRatings[i].Players.Single().Mu, newRatings[i].Players.Single().Sigma);
                    await db.UpdateMatchmakingUserStatsAsync(stats[i]);
                }
            }
        }

        protected override Task Finish()
        {
            return Task.CompletedTask;
        }
    }
}
