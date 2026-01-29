// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class CardDiscardStageTests : RankedPlayStageImplementationTest
    {
        public CardDiscardStageTests()
            : base(RankedPlayStage.CardDiscard)
        {
        }

        [Fact]
        public async Task AllPlayersCanDiscard()
        {
            Receiver.Invocations.Clear();
            UserReceiver.Invocations.Clear();
            User2Receiver.Invocations.Clear();

            SetUserContext(ContextUser);
            await Hub.DiscardCards(UserState.Hand.Take(2).ToArray());

            Receiver.Verify(u => u.RankedPlayCardRemoved(USER_ID, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            Receiver.Verify(u => u.RankedPlayCardAdded(USER_ID, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(2));
            User2Receiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
            Receiver.Invocations.Clear();
            UserReceiver.Invocations.Clear();
            User2Receiver.Invocations.Clear();

            SetUserContext(ContextUser2);
            await Hub.DiscardCards(User2State.Hand.Take(2).ToArray());

            Receiver.Verify(u => u.RankedPlayCardRemoved(USER_ID_2, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            Receiver.Verify(u => u.RankedPlayCardAdded(USER_ID_2, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
            User2Receiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CanNotDiscardMultipleTimes()
        {
            await Hub.DiscardCards(UserState.Hand.Take(2).ToArray());
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.DiscardCards(UserState.Hand.Take(2).ToArray()));
        }

        [Fact]
        public async Task CanNotDiscardCardNotInHand()
        {
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.DiscardCards([new RankedPlayCardItem()]));
        }

        [Fact]
        public async Task ContinuesToFinishCardDiscardAfterAllPlayersDiscard()
        {
            SetUserContext(ContextUser);
            await Hub.DiscardCards(UserState.Hand.Take(2).ToArray());
            Assert.Equal(RankedPlayStage.CardDiscard, RoomState.Stage);

            SetUserContext(ContextUser2);
            await Hub.DiscardCards(User2State.Hand.Take(2).ToArray());
            Assert.Equal(RankedPlayStage.CardDiscard, RoomState.Stage);

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.FinishCardDiscard, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToFinishCardDiscardWithoutDiscard()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.FinishCardDiscard, RoomState.Stage);
        }
    }
}
