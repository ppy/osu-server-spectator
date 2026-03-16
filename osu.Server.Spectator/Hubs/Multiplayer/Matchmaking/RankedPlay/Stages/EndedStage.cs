// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using osu.Game.Online.Multiplayer;
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
            foreach ((_, RankedPlayUserInfo user) in State.Users)
                user.RatingAfter = user.Rating;

            // Forego any rating calculations if the match hasn't started yet.
            // Naturally, this also means we don't have a winner to crown.
            if (State.CurrentRound == 0)
                return;

            int maxLife = State.Users.Max(u => u.Value.Life);
            var winners = State.Users.Where(u => u.Value.Life == maxLife).ToArray();
            if (winners.Length == 1)
                State.WinningUserId = winners[0].Key;

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
                List<double> scores = [];

                foreach ((int userId, RankedPlayUserInfo user) in State.Users)
                {
                    matchmaking_user_stats userStats = await db.GetMatchmakingUserStatsAsync(userId, Controller.PoolId) ?? new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        pool_id = Controller.PoolId
                    };

                    stats.Add(userStats);
                    teams.Add(new Team { Players = [model.Rating(userStats.EloData.Rating.Mu, userStats.EloData.Rating.Sig)] });
                    scores.Add(user.Life);
                }

                IRating[] newRatings = model.Rate(teams, scores: scores).Select(t => t.Players.Single()).ToArray();

                for (int i = 0; i < stats.Count; i++)
                {
                    matchmaking_room_result result;

                    if (State.WinningUserId == null)
                        result = matchmaking_room_result.draw;
                    else if (State.WinningUserId == stats[i].user_id)
                        result = matchmaking_room_result.win;
                    else
                        result = matchmaking_room_result.loss;

                    await db.InsertUserEloHistoryEntry(
                        (ulong)Room.RoomID,
                        Controller.PoolId,
                        stats[i].user_id,
                        stats.First(u => u.user_id != stats[i].user_id).user_id,
                        result,
                        (int)Math.Round(stats[i].EloData.Rating.Mu),
                        (int)Math.Round(newRatings[i].Mu));

                    stats[i].EloData.ContestCount++;
                    stats[i].EloData.Rating = new EloRating(newRatings[i].Mu, newRatings[i].Sigma);
                    await db.UpdateMatchmakingUserStatsAsync(stats[i]);

                    State.Users[(int)stats[i].user_id].RatingAfter = (int)Math.Round(newRatings[i].Mu);
                }
            }
        }

        protected override Task Finish()
        {
            return Task.CompletedTask;
        }

        public override Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // The match is over, no need to kill users.
            return Task.CompletedTask;
        }
    }
}
