// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
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
        protected override TimeSpan Duration => TimeSpan.FromSeconds(20);

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

            int maxTotalScore = (int)scores.Select(s => s.total_score).Max();

            foreach (var score in scores)
            {
                var userInfo = State.Users[(int)score.user_id];

                int rawDamage = maxTotalScore - (int)score.total_score;
                int damage = (int)Math.Ceiling(rawDamage * State.DamageMultiplier);

                int oldLife = userInfo.Life;
                int newLife = Math.Max(0, oldLife - damage);

                userInfo.Life = newLife;
                userInfo.DamageInfo = new RankedPlayDamageInfo
                {
                    RawDamage = rawDamage,
                    Damage = damage,
                    OldLife = oldLife,
                    NewLife = newLife,
                };
            }
        }

        protected override async Task Finish()
        {
            foreach ((_, RankedPlayUserInfo userInfo) in State.Users)
                userInfo.DamageInfo = null;

            if (hasGameplayRoundsRemaining())
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
            else
                await Controller.GotoStage(RankedPlayStage.Ended);
        }

        public override async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // Allow players to leave early without incurring a loss if they know gameplay won't continue.
            if (hasGameplayRoundsRemaining())
                await KillUser(user);

            // Remain in the results stage, which will naturally transition to the ended stage once the countdown expires.
        }

        private bool hasGameplayRoundsRemaining()
        {
            int countPlayersAlive = State.Users.Count(u => u.Value.Life > 0);
            int countCardsRemaining = Controller.DeckCount + State.Users.Sum(u => u.Value.Hand.Count);
            return countPlayersAlive > 1 && countCardsRemaining > 0;
        }
    }
}
