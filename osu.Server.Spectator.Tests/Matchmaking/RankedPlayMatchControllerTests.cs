// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class RankedPlayMatchControllerTests : MultiplayerTest, IAsyncLifetime
    {
        public RankedPlayMatchControllerTests()
        {
            AppSettings.MatchmakingRoomRounds = 2;
            AppSettings.MatchmakingRoomAllowSkip = true;

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(roomId => InitialiseRoom(roomId, 20))
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.ranked_play,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!),
                    });

            Database.Setup(db => db.GetMatchmakingUserStatsAsync(It.IsAny<int>(), It.IsAny<uint>()))
                    .Returns<int, uint>((userId, poolId) => Task.FromResult<matchmaking_user_stats?>(new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        pool_id = poolId
                    }));
        }

        public async Task InitializeAsync()
        {
            using (var room = await Rooms.GetForUse(ROOM_ID, true))
                room.Item = await MatchmakingQueueBackgroundService.InitialiseRoomAsync(ROOM_ID, HubContext, DatabaseFactory.Object, EventLogger, [USER_ID, USER_ID_2], 0);
        }

        [Fact]
        public async Task NormalRoomFlow()
        {
            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>())).Returns(() => Task.FromResult((IEnumerable<SoloScore>)
            [
                new SoloScore
                {
                    user_id = USER_ID,
                    total_score = 800_000
                },
                new SoloScore
                {
                    user_id = USER_ID_2,
                    total_score = 500_000
                }
            ]));

            var room = Rooms.GetEntityUnsafe(ROOM_ID)!;
            Assert.IsType<RankedPlayRoomState>(Rooms.GetEntityUnsafe(ROOM_ID)!.MatchState);
            var roomState = (RankedPlayRoomState)room.MatchState!;

            await verifyStage(RankedPlayStage.WaitForJoin);

            // Join the first user.

            UserReceiver.Invocations.Clear();

            await Hub.JoinRoom(ROOM_ID);
            var userState = roomState.Users[0];

            Assert.Equal(5, userState.Hand.Count);
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(5));
            await verifyStage(RankedPlayStage.WaitForJoin);

            // Join the second user.

            UserReceiver.Invocations.Clear();
            User2Receiver.Invocations.Clear();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            var userState2 = roomState.Users[1];

            Assert.Equal(5, userState2.Hand.Count);
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
            User2Receiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(5));

            // Warmup stage.

            await verifyStage(RankedPlayStage.RoundWarmup);
            Assert.True(roomState.ActiveUserId >= 0);

            // Discard stage.

            await gotoNextStage();
            await verifyStage(RankedPlayStage.CardDiscard);

            // First user discards two cards.

            Receiver.Invocations.Clear();
            UserReceiver.Invocations.Clear();
            User2Receiver.Invocations.Clear();

            SetUserContext(ContextUser);
            await Hub.DiscardCards(userState.Hand.Take(2).ToArray());

            Receiver.Verify(u => u.RankedPlayCardRemoved(USER_ID, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            Receiver.Verify(u => u.RankedPlayCardAdded(USER_ID, It.IsAny<RankedPlayCardItem>()), Times.Exactly(2));
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(2));
            User2Receiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Never);

            // Second user discards no cards.

            Receiver.Invocations.Clear();

            SetUserContext(ContextUser2);
            await Hub.DiscardCards([]);

            Receiver.Verify(u => u.RankedPlayCardRemoved(USER_ID_2, It.IsAny<RankedPlayCardItem>()), Times.Never);
            Receiver.Verify(u => u.RankedPlayCardAdded(USER_ID_2, It.IsAny<RankedPlayCardItem>()), Times.Never);

            // Both players have finished discarding.

            await verifyStage(RankedPlayStage.FinishCardDiscard);
            await gotoNextStage();

            // Select stage.

            await verifyStage(RankedPlayStage.CardPlay);

            // Active player plays a card.

            Receiver.Invocations.Clear();

            (Mock<HubCallerContext> context, RankedPlayUserInfo state, MultiplayerRoomUser user) activePlayer = roomState.ActiveUserId switch
            {
                USER_ID => (ContextUser, userState, room.Users[0]),
                USER_ID_2 => (ContextUser2, userState2, room.Users[1]),
                _ => throw new ArgumentOutOfRangeException()
            };

            RankedPlayCardItem activeCard = activePlayer.state.Hand[0];

            SetUserContext(activePlayer.context);
            await Hub.PlayCard(activeCard);

            Receiver.Verify(u => u.RankedPlayCardPlayed(activeCard), Times.Once);

            // Player has finished selecting.

            await verifyStage(RankedPlayStage.FinishCardPlay);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            // Both players have downloaded the beatmap and readied up.

            await verifyStage(RankedPlayStage.GameplayWarmup);
            await gotoNextStage();

            // Start gameplay for both users.

            await verifyStage(RankedPlayStage.Gameplay);
            await gotoNextStage();

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            // End gameplay for both users

            Receiver.Invocations.Clear();

            SetUserContext(ContextUser);
            await Hub.AbortGameplay();
            SetUserContext(ContextUser2);
            await Hub.AbortGameplay();

            // Results stage.

            await verifyStage(RankedPlayStage.Results);

            Assert.Equal(1_000_000, userState.Life);
            Assert.Equal(700_000, userState2.Life);
            Receiver.Verify(u => u.RankedPlayCardRemoved(activePlayer.user.UserID, activeCard), Times.Once);

            // Next round.

            await gotoNextStage();
            await verifyStage(RankedPlayStage.RoundWarmup);

            // No discard stage in the second round.

            await gotoNextStage();
            await verifyStage(RankedPlayStage.CardPlay);
        }

        [Fact]
        public async Task OnlyActivePlayerCanPlayCard()
        {
            var room = Rooms.GetEntityUnsafe(ROOM_ID)!;
            var roomState = (RankedPlayRoomState)room.MatchState!;

            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(RankedPlayStage.CardPlay);

            (Mock<HubCallerContext> context, RankedPlayUserInfo state) inactivePlayer = roomState.ActiveUserId switch
            {
                USER_ID => (ContextUser2, roomState.Users[1]),
                USER_ID_2 => (ContextUser, roomState.Users[0]),
                _ => throw new ArgumentOutOfRangeException()
            };

            SetUserContext(inactivePlayer.context);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.PlayCard(inactivePlayer.state.Hand[0]));
        }

        private async Task verifyStage(RankedPlayStage stage)
        {
            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(stage, ((RankedPlayRoomState)room.Item!.MatchState!).Stage);
        }

        private async Task gotoNextStage()
        {
            RankedPlayMatchController controller;

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                controller = (RankedPlayMatchController)room.Item.Controller;
            }

            controller.SkipToNextStage(out Task countdownTask);
            await countdownTask;
        }

        private async Task gotoStage(RankedPlayStage stage)
        {
            while (true)
            {
                RankedPlayStage currentStage;

                using (var room = await Rooms.GetForUse(ROOM_ID))
                    currentStage = ((RankedPlayRoomState)room.Item!.MatchState!).Stage;

                if (currentStage == stage)
                    break;

                switch (currentStage)
                {
                    case RankedPlayStage.RoundWarmup:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.WaitForJoin:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.CardDiscard:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.FinishCardDiscard:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.CardPlay:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.FinishCardPlay:
                        SetUserContext(ContextUser);
                        await Hub.ChangeState(MultiplayerUserState.Ready);
                        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
                        break;

                    case RankedPlayStage.GameplayWarmup:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.Gameplay:
                        SetUserContext(ContextUser);
                        await Hub.ChangeState(MultiplayerUserState.Loaded);
                        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                        await Hub.AbortGameplay();
                        break;

                    case RankedPlayStage.Results:
                        await gotoNextStage();
                        break;

                    case RankedPlayStage.Ended:
                        await gotoNextStage();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(stage));
                }
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
