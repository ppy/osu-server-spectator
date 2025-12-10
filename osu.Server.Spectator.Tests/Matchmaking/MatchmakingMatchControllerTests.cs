// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingMatchControllerTests : MultiplayerTest, IAsyncLifetime
    {
        public MatchmakingMatchControllerTests()
        {
            AppSettings.MatchmakingRoomRounds = 2;
            AppSettings.MatchmakingRoomAllowSkip = true;
            AppSettings.MatchmakingHeadToHeadIsBestOf = false;

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(roomId => InitialiseRoom(roomId, 10))
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.matchmaking,
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
                    total_score = 10
                },
                new SoloScore
                {
                    user_id = USER_ID_2,
                    total_score = 5
                }
            ]));

            // Join the first user.
            await Hub.JoinRoom(ROOM_ID);
            Assert.Null(Rooms.GetEntityUnsafe(ROOM_ID)!.Host);

            // Check that the room is waiting for users.
            await verifyStage(MatchmakingStage.WaitingForClientsJoin);

            // Check the user received the matchmaking state.
            Receiver.Verify(u => u.MatchRoomStateChanged(It.Is<MatchmakingRoomState>(s => s.Stage == MatchmakingStage.WaitingForClientsJoin)), Times.AtLeastOnce());
            Receiver.Invocations.Clear();

            // Join the second user.
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            Assert.Null(Rooms.GetEntityUnsafe(ROOM_ID)!.Host);

            for (int i = 1; i <= 2; i++)
            {
                // Check that the room is now in the warmup stage.
                await verifyStage(MatchmakingStage.RoundWarmupTime);

                // Check that both users received the new state
                int round = i;
                Receiver.Verify(u => u.MatchRoomStateChanged(It.Is<MatchmakingRoomState>(s => s.Stage == MatchmakingStage.RoundWarmupTime && s.CurrentRound == round)), Times.Once);

                // Begin beatmap selection.
                await gotoNextStage();
                await verifyStage(MatchmakingStage.UserBeatmapSelect);

                // Select a beatmap for both users.
                long[] selectedPlaylistItems;
                using (var room = await Rooms.GetForUse(ROOM_ID))
                    selectedPlaylistItems = room.Item!.Playlist.Where(item => !item.Expired).Skip(3).Take(2).Select(item => item.ID).ToArray();
                SetUserContext(ContextUser);
                await Hub.MatchmakingToggleSelection(selectedPlaylistItems[0]);
                SetUserContext(ContextUser2);
                await Hub.MatchmakingToggleSelection(selectedPlaylistItems[1]);

                // Finalise beatmap selection.
                await gotoNextStage();
                await verifyStage(MatchmakingStage.ServerBeatmapFinalised);

                // Begin beatmap downloads.
                await gotoNextStage();
                await verifyStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

                // Ready up both users.
                SetUserContext(ContextUser);
                await MarkCurrentUserReadyAndAvailable();
                SetUserContext(ContextUser2);
                await MarkCurrentUserReadyAndAvailable();

                // Check that the room continued to the next stage because all players downloaded the beatmap.
                await verifyStage(MatchmakingStage.GameplayWarmupTime);

                // Begin gameplay.
                await gotoNextStage();
                await verifyStage(MatchmakingStage.Gameplay);

                long playlistItemId;
                using (var room = await Rooms.GetForUse(ROOM_ID))
                    playlistItemId = room.Item!.CurrentPlaylistItem.ID;

                // Check that a request to load gameplay was started.
                Receiver.Verify(u => u.LoadRequested(), Times.Once);

                // Start gameplay for both users.
                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

                Database.Verify(db => db.LogRoomEventAsync(
                    It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_started" && ev.playlist_item_id == playlistItemId)), Times.Once);

                // End gameplay for both users
                SetUserContext(ContextUser);
                await Hub.AbortGameplay();
                SetUserContext(ContextUser2);
                await Hub.AbortGameplay();

                Database.Verify(db => db.LogRoomEventAsync(
                    It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_aborted" && ev.playlist_item_id == playlistItemId)), Times.Once);

                // Check that the room continued to show the results after gameplay.
                await verifyStage(MatchmakingStage.ResultsDisplaying);

                // Check that the standings were updated.
                using (var room = await Rooms.GetForUse(ROOM_ID))
                {
                    Assert.Equal(15 * i, ((MatchmakingRoomState)room.Item!.MatchState!).Users.GetOrAdd(USER_ID).Points);
                    Assert.Equal(1, ((MatchmakingRoomState)room.Item!.MatchState!).Users.GetOrAdd(USER_ID).Placement);

                    Assert.Equal(12 * i, ((MatchmakingRoomState)room.Item!.MatchState!).Users.GetOrAdd(USER_ID_2).Points);
                    Assert.Equal(2, ((MatchmakingRoomState)room.Item!.MatchState!).Users.GetOrAdd(USER_ID_2).Placement);
                }

                Receiver.Invocations.Clear();
                await gotoNextStage();
            }

            await verifyStage(MatchmakingStage.Ended);
        }

        [Fact]
        public async Task RoundStartsIfUsersDoNotJoin()
        {
            await Hub.JoinRoom(ROOM_ID);
            await verifyStage(MatchmakingStage.WaitingForClientsJoin);

            await gotoNextStage();
            await verifyStage(MatchmakingStage.RoundWarmupTime);
        }

        [Fact]
        public async Task GameplayDoesNotStartIfNoUsersReady()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            await gotoNextStage();
            await verifyStage(MatchmakingStage.WaitingForClientsBeatmapDownload);
        }

        [Fact]
        public async Task GameplayStartsIfAnyUserReady()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            // Ready up a single user.
            await MarkCurrentUserReadyAndAvailable();

            await gotoNextStage();
            await verifyStage(MatchmakingStage.GameplayWarmupTime);
        }

        [Fact]
        public async Task PlaylistItemExpiredOnCompletion()
        {
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.ResultsDisplaying);

            Database.Verify(db => db.MarkPlaylistItemAsPlayedAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Once);
            Receiver.Verify(u => u.PlaylistItemChanged(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);

            await gotoStage(MatchmakingStage.Ended);

            Database.Verify(db => db.MarkPlaylistItemAsPlayedAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Exactly(2));
            Receiver.Verify(u => u.PlaylistItemChanged(It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(2));
        }

        /// <summary>
        /// Tests that when multiple users pick the same candidate playlist item, it's only included once in the final selection.
        /// </summary>
        [Fact]
        public async Task DuplicateCandidateIncludedOnce()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.UserBeatmapSelect);

            long playlistItemId;
            using (var room = await Rooms.GetForUse(ROOM_ID))
                playlistItemId = room.Item!.Playlist.First().ID;

            // Both users select the same playlist item
            SetUserContext(ContextUser);
            await Hub.MatchmakingToggleSelection(playlistItemId);
            SetUserContext(ContextUser2);
            await Hub.MatchmakingToggleSelection(playlistItemId);

            await gotoStage(MatchmakingStage.ServerBeatmapFinalised);

            // Check that only one item appears in the candidates.
            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Single(((MatchmakingRoomState)room.Item!.MatchState!).CandidateItems);
        }

        [Fact]
        public async Task CanNotInvitePlayersToRoom()
        {
            Database.Setup(d => d.GetUserRelation(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new phpbb_zebra { friend = true });

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.InvitePlayer(USER_ID_2));
        }

        /// <summary>
        /// Tests that the stage advances when users leave, particularly in the case where the server is waiting on one to download the beatmap.
        /// </summary>
        [Fact]
        public async Task StageAdvancesWhenUsersLeaveDuringDownload()
        {
            CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);

            using (var room = await Rooms.GetForUse(ROOM_ID, true))
                room.Item = await MatchmakingQueueBackgroundService.InitialiseRoomAsync(ROOM_ID, HubContext, DatabaseFactory.Object, EventLogger, [USER_ID, USER_ID_2, 3], 0);

            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            // Ready up the first and second users.
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();

            // Quit the third user.
            SetUserContext(contextUser3);
            await Hub.LeaveRoom();

            await verifyStage(MatchmakingStage.GameplayWarmupTime);
        }

        /// <summary>
        /// Tests that only the user beatmap picks will be the candidate items, when there are any user picks.
        /// </summary>
        [Fact]
        public async Task UserPicksUsedForRandomSelection()
        {
            CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);

            using (var room = await Rooms.GetForUse(ROOM_ID, true))
                room.Item = await MatchmakingQueueBackgroundService.InitialiseRoomAsync(ROOM_ID, HubContext, DatabaseFactory.Object, EventLogger, [USER_ID, USER_ID_2, 3], 0);

            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);

            long[] userPickIds;
            using (var room = await Rooms.GetForUse(ROOM_ID))
                userPickIds = room.Item!.Playlist.Take(2).Select(i => i.ID).ToArray();

            await gotoStage(MatchmakingStage.UserBeatmapSelect);

            // Players 1 and 2 select a beatmap, player 3 doesn't.
            SetUserContext(ContextUser);
            await Hub.MatchmakingToggleSelection(userPickIds[0]);
            SetUserContext(ContextUser2);
            await Hub.MatchmakingToggleSelection(userPickIds[1]);

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Equal(userPickIds, ((MatchmakingRoomState)room.Item!.MatchState!).CandidateItems);
                Assert.True(((MatchmakingRoomState)room.Item!.MatchState!).CandidateItem > 0);
            }
        }

        /// <summary>
        /// Tests that when no user picks a beatmap, the server will select one beatmap at random.
        /// </summary>
        [Fact]
        public async Task NoUserPicksServerSelectsBeatmap()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Single(((MatchmakingRoomState)room.Item!.MatchState!).CandidateItems);
        }

        [Fact]
        public async Task IneligibleUserCanNotJoinRoom()
        {
            CreateUser(4, out Mock<HubCallerContext> contextUser4, out _);

            SetUserContext(contextUser4);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.JoinRoom(ROOM_ID));
        }

        [Fact]
        public async Task RandomSelection()
        {
            await Hub.JoinRoom(ROOM_ID);

            await gotoStage(MatchmakingStage.UserBeatmapSelect);
            using (var room = await Rooms.GetForUse(ROOM_ID))
                room.Item!.Settings.PlaylistItemId = 0;

            await Hub.MatchmakingToggleSelection(-1);
            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Equal([-1], ((MatchmakingRoomState)room.Item!.MatchState!).CandidateItems);
                Assert.Equal(-1, ((MatchmakingRoomState)room.Item!.MatchState!).CandidateItem);
                Assert.NotEqual(-1, ((MatchmakingRoomState)room.Item!.MatchState!).GameplayItem);
                Assert.True(room.Item!.Settings.PlaylistItemId > 0);
            }
        }

        [Fact]
        public async Task RoomEndsWithSinglePlayer()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.LeaveRoom();

            await verifyStage(MatchmakingStage.Ended);
        }

        [Fact]
        public async Task StatsUpdateAfterForcefulEnd()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            var room = Rooms.GetEntityUnsafe(ROOM_ID)!;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID).Points = 10;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID_2).Points = 5;

            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            SetUserContext(ContextUser);
            await Hub.LeaveRoom();

            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(It.IsAny<matchmaking_user_stats>()), Times.Exactly(2));
        }

        [Fact]
        public async Task StatsUpdatesIfAllPlayersLeaveDuringGameplay()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            var room = Rooms.GetEntityUnsafe(ROOM_ID)!;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID).Points = 10;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID_2).Points = 5;

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            // Enter gameplay for both users.
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();

            await gotoStage(MatchmakingStage.Gameplay);

            // Leave both users.
            SetUserContext(ContextUser);
            await Hub.LeaveRoom();
            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(It.IsAny<matchmaking_user_stats>()), Times.Exactly(2));
        }

        [Fact]
        public async Task MatchDoesNotEndDuringGameplay()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            var room = Rooms.GetEntityUnsafe(ROOM_ID)!;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID).Points = 10;
            ((MatchmakingRoomState)room.MatchState!).Users.GetOrAdd(USER_ID_2).Points = 5;

            await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

            // Enter gameplay for both users.
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();

            await gotoStage(MatchmakingStage.Gameplay);

            // Leave user 2.
            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            // Match should still be "ongoing".
            await verifyStage(MatchmakingStage.Gameplay);
            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(It.IsAny<matchmaking_user_stats>()), Times.Never);

            SetUserContext(ContextUser);
            await Hub.AbortGameplay();

            await verifyStage(MatchmakingStage.ResultsDisplaying);
            await gotoNextStage();

            await verifyStage(MatchmakingStage.Ended);
            Database.Verify(db => db.UpdateMatchmakingUserStatsAsync(It.IsAny<matchmaking_user_stats>()), Times.Exactly(2));
        }

        [Fact]
        public async Task HeadToHeadMatchEndsEarlyWhenScoreIsNotAttainable()
        {
            AppSettings.MatchmakingRoomRounds = 5;
            AppSettings.MatchmakingHeadToHeadIsBestOf = true;

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>())).Returns(() => Task.FromResult((IEnumerable<SoloScore>)
            [
                new SoloScore
                {
                    user_id = USER_ID,
                    total_score = 10
                },
                new SoloScore
                {
                    user_id = USER_ID_2,
                    total_score = 5
                }
            ]));

            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            for (int i = 0; i < 3; i++)
            {
                await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

                // Enter gameplay for both users.
                SetUserContext(ContextUser);
                await MarkCurrentUserReadyAndAvailable();
                SetUserContext(ContextUser2);
                await MarkCurrentUserReadyAndAvailable();

                await gotoStage(MatchmakingStage.Gameplay);

                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                await Hub.AbortGameplay();

                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                await Hub.AbortGameplay();

                await gotoNextStage();
            }

            await verifyStage(MatchmakingStage.Ended);
        }

        [Fact]
        public async Task BestOfFullRounds()
        {
            AppSettings.MatchmakingRoomRounds = 5;
            AppSettings.MatchmakingHeadToHeadIsBestOf = true;

            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            for (int i = 0; i < 5; i++)
            {
                int i2 = i;
                Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>())).Returns(() => Task.FromResult((IEnumerable<SoloScore>)
                [
                    new SoloScore
                    {
                        user_id = USER_ID,
                        total_score = i2 < 2 ? 10u : 5u
                    },
                    new SoloScore
                    {
                        user_id = USER_ID_2,
                        total_score = i2 < 2 ? 5u : 10u
                    }
                ]));

                await gotoStage(MatchmakingStage.WaitingForClientsBeatmapDownload);

                // Enter gameplay for both users.
                SetUserContext(ContextUser);
                await MarkCurrentUserReadyAndAvailable();
                SetUserContext(ContextUser2);
                await MarkCurrentUserReadyAndAvailable();

                await gotoStage(MatchmakingStage.Gameplay);

                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                await Hub.AbortGameplay();

                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                await Hub.AbortGameplay();

                await gotoNextStage();
            }

            await verifyStage(MatchmakingStage.Ended);
        }

        private async Task verifyStage(MatchmakingStage stage)
        {
            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(stage, ((MatchmakingRoomState)room.Item!.MatchState!).Stage);
        }

        private async Task gotoNextStage()
        {
            MatchmakingMatchController controller;

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                controller = (MatchmakingMatchController)room.Item.Controller;
            }

            controller.SkipToNextStage(out Task countdownTask);
            await countdownTask;
        }

        private async Task gotoStage(MatchmakingStage stage)
        {
            while (true)
            {
                MatchmakingStage currentStage;

                using (var room = await Rooms.GetForUse(ROOM_ID))
                    currentStage = ((MatchmakingRoomState)room.Item!.MatchState!).Stage;

                if (currentStage == stage)
                    break;

                switch (currentStage)
                {
                    case MatchmakingStage.WaitingForClientsJoin:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.RoundWarmupTime:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.UserBeatmapSelect:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.ServerBeatmapFinalised:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.WaitingForClientsBeatmapDownload:
                        SetUserContext(ContextUser);
                        await Hub.ChangeState(MultiplayerUserState.Ready);
                        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
                        break;

                    case MatchmakingStage.GameplayWarmupTime:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.Gameplay:
                        SetUserContext(ContextUser);
                        await Hub.ChangeState(MultiplayerUserState.Loaded);
                        await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                        await Hub.AbortGameplay();
                        break;

                    case MatchmakingStage.ResultsDisplaying:
                        await gotoNextStage();
                        break;

                    case MatchmakingStage.Ended:
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
