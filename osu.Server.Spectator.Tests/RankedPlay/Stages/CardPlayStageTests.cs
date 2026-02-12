// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
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
            Assert.Single(UserState.Hand);
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

            Assert.Contains(card, UserState.Hand);
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

        [Fact]
        public async Task CardSelectionViaReplayIsActivatedOnCountdownEnd()
        {
            RoomState.ActiveUserId = USER_ID;

            var card = UserState.Hand.ElementAt(1);
            await Hub.SendMatchRequest(new RankedPlayCardHandReplayRequest
            {
                Frames = new[]
                {
                    new RankedPlayCardHandReplayFrame
                    {
                        Delay = 0,
                        Cards = new Dictionary<Guid, RankedPlayCardState>
                        {
                            [card.ID] = new RankedPlayCardState
                            {
                                Selected = true,
                                Hovered = false,
                                Pressed = false,
                            }
                        }
                    }
                }
            });

            await FinishCountdown();

            Assert.Equal(MatchController.LastActivatedCard, card);
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            Receiver.Verify(r => r.RankedPlayCardPlayed(card), Times.Once);
        }

        [Fact]
        public async Task ReplayFramesFromInactivePlayerDoNotAffectSelection()
        {
            RoomState.ActiveUserId = USER_ID;

            var card = User2State.Hand.First();

            SetUserContext(ContextUser2);
            await Hub.SendMatchRequest(new RankedPlayCardHandReplayRequest
            {
                Frames = new[]
                {
                    new RankedPlayCardHandReplayFrame
                    {
                        Delay = 0,
                        Cards = new Dictionary<Guid, RankedPlayCardState>
                        {
                            [card.ID] = new RankedPlayCardState
                            {
                                Selected = true,
                                Hovered = false,
                                Pressed = false,
                            }
                        }
                    }
                }
            });

            await FinishCountdown();

            Assert.NotEqual(MatchController.LastActivatedCard, card);
        }

        [Fact]
        public async Task PlayedCardTakesPrecedenceOverReplaySelection()
        {
            RoomState.ActiveUserId = USER_ID;

            var replayCard = UserState.Hand.ElementAt(1);
            var playedCard = UserState.Hand.ElementAt(2);

            await Hub.SendMatchRequest(new RankedPlayCardHandReplayRequest
            {
                Frames = new[]
                {
                    new RankedPlayCardHandReplayFrame
                    {
                        Delay = 0,
                        Cards = new Dictionary<Guid, RankedPlayCardState>
                        {
                            [replayCard.ID] = new RankedPlayCardState
                            {
                                Selected = true,
                                Hovered = false,
                                Pressed = false,
                            }
                        }
                    }
                }
            });

            await Hub.PlayCard(playedCard);

            Assert.Equal(MatchController.LastActivatedCard, playedCard);
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            Receiver.Verify(r => r.RankedPlayCardPlayed(playedCard), Times.Once);
            Receiver.Verify(r => r.RankedPlayCardPlayed(replayCard), Times.Never);
        }

        [Fact]
        public async Task InvalidCardIdInReplayFrameDoesNotAffectSelection()
        {
            RoomState.ActiveUserId = USER_ID;

            Guid cardId = Guid.NewGuid();

            await Hub.SendMatchRequest(new RankedPlayCardHandReplayRequest
            {
                Frames = new[]
                {
                    new RankedPlayCardHandReplayFrame
                    {
                        Delay = 0,
                        Cards = new Dictionary<Guid, RankedPlayCardState>
                        {
                            [cardId] = new RankedPlayCardState
                            {
                                Selected = true,
                                Hovered = false,
                                Pressed = false,
                            }
                        }
                    }
                }
            });

            await FinishCountdown();

            Assert.NotEqual(MatchController.LastActivatedCard?.ID, cardId);
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);
        }
    }
}
