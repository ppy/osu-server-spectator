// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class CardPlayStage : RankedPlayStageImplementation
    {
        public CardPlayStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.CardPlay;
        protected override TimeSpan Duration => TimeSpan.FromSeconds(30);

        private RankedPlayCardItem? playedCard;

        protected override async Task Begin()
        {
            Debug.Assert(State.ActiveUserId != null);
            Debug.Assert(State.ActiveUser != null);

            if (State.ActiveUser.Hand.Count == 0)
                await Controller.AddCards(State.ActiveUserId.Value, 1);
        }

        protected override async Task Finish()
        {
            Debug.Assert(State.ActiveUserId != null);
            Debug.Assert(State.ActiveUser != null);

            await Controller.ActivateCard(playedCard ?? State.ActiveUser.Hand.First());
            await Controller.GotoStage(RankedPlayStage.FinishCardPlay);
        }

        public override async Task HandlePlayCard(MultiplayerRoomUser user, RankedPlayCardItem card)
        {
            if (user.UserID != State.ActiveUserId)
                throw new InvalidStateException("Not the active player.");

            if (!State.Users[user.UserID].Hand.Contains(card))
                throw new InvalidStateException("Card not in the hand.");

            if (playedCard != null)
                return;

            playedCard = card;
            await Finish();
        }
    }
}
