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
    public class CardPlayStageTests : RankedPlayStageImplementationTest
    {
        public CardPlayStageTests()
            : base(RankedPlayStage.CardPlay)
        {
        }

        [Fact]
        public async Task CardDrawnWhenHandEmpty()
        {
            RoomState.ActiveUserId = USER_ID;
            UserState.Hand.Clear();

            await MatchController.GotoStage(RankedPlayStage.CardPlay);
            Assert.Equal(1, UserState.Hand.Count);
        }

        [Fact]
        public async Task ContinuesToFinishCardPlayOnCardPlayed()
        {
            RoomState.ActiveUserId = USER_ID;

            var card = UserState.Hand.First();
            await Hub.PlayCard(card);

            Assert.Equal(card, MatchController.LastActivatedCard);
            Assert.NotEqual(0, Room.Settings.PlaylistItemId);
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            Receiver.Verify(r => r.RankedPlayCardPlayed(card), Times.Once);
            Receiver.Verify(r => r.RankedPlayCardRevealed(card, It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
        }

        [Fact]
        public async Task CardAutomaticallyPlayedOnCountdownEnd()
        {
            RoomState.ActiveUserId = USER_ID;

            var card = UserState.Hand.First();
            await FinishCountdown();

            Assert.True(UserState.Hand.Any(c => c.Equals(card)));
            Assert.NotEqual(0, Room.Settings.PlaylistItemId);
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            Receiver.Verify(r => r.RankedPlayCardPlayed(card), Times.Once);
            Receiver.Verify(r => r.RankedPlayCardRevealed(card, It.IsAny<MultiplayerPlaylistItem>()), Times.Once);
        }

        [Fact]
        public async Task CanNotPlayCardNotInHand()
        {
            RoomState.ActiveUserId = USER_ID;
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.PlayCard(new RankedPlayCardItem()));
        }

        [Fact]
        public async Task InactivePlayerCanNotPlayCard()
        {
            RoomState.ActiveUserId = USER_ID_2;
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.PlayCard(UserState.Hand.First()));
        }
    }
}
