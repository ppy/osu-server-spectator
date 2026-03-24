// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class RoundWarmupStage : RankedPlayStageImplementation
    {
        public RoundWarmupStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.RoundWarmup;

        protected override TimeSpan Duration => State.CurrentRound == 1 ? TimeSpan.FromSeconds(20) : TimeSpan.Zero;

        protected override async Task Begin()
        {
            foreach (var user in Room.Users.Where(u => u.State != MultiplayerUserState.Idle))
            {
                await Room.ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
                await Room.UpdateRoomStateIfRequired();
            }

            State.CurrentRound++;
            State.DamageMultiplier = computeDamageMultiplier(State.CurrentRound);

            // Activate the next player.
            // For the first round, this is set during room initialisation.
            if (State.CurrentRound >= 2)
                State.ActiveUserId = Controller.UserIdsByTurnOrder.Concat(Controller.UserIdsByTurnOrder).SkipWhile(u => u != State.ActiveUserId).Skip(1).First();

            // Draw a card on the player's next (non-first) turn.
            if (State.CurrentRound >= 3)
                await Controller.AddCards(State.ActiveUserId!.Value, 1);
        }

        protected override async Task Finish()
        {
            if (State.CurrentRound == 1)
                await Controller.GotoStage(RankedPlayStage.CardDiscard);
            else
                await Controller.GotoStage(RankedPlayStage.CardPlay);
        }

        /// <summary>
        /// Retrieves the damage multiplier for a given round.
        /// </summary>
        /// <param name="round">The round.</param>
        private static double computeDamageMultiplier(int round)
        {
            if (round <= 2)
                return 1;

            return 2 + (round - 3) * 0.5;
        }
    }
}
