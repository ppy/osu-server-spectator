// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class CardDiscardStage : RankedPlayStageImplementation
    {
        public CardDiscardStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.CardDiscard;
        protected override TimeSpan Duration => TimeSpan.FromSeconds(30);

        private readonly HashSet<int> userCardsDiscarded = [];

        protected override Task Begin()
        {
            return Task.CompletedTask;
        }

        protected override async Task Finish()
        {
            await Controller.GotoStage(RankedPlayStage.FinishCardDiscard);
        }

        public override async Task HandleDiscardCards(MultiplayerRoomUser user, RankedPlayCardItem[] cards)
        {
            if (!userCardsDiscarded.Add(user.UserID))
                throw new InvalidStateException("Cards have already been discarded.");

            if (!cards.All(State.Users[user.UserID].Hand.Contains))
                throw new InvalidStateException("One or more cards were not in the hand.");

            await Controller.RemoveCards(user.UserID, cards);
            await Controller.AddCards(user.UserID, cards.Length);

            // When both users have finished discarding their cards, wait for animations to complete before transitioning the stage.
            if (userCardsDiscarded.Count == State.Users.Count)
                await FinishWithCountdown(TimeSpan.FromSeconds(3));
        }
    }
}
