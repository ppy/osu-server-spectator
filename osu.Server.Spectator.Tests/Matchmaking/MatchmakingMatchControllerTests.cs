// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingMatchControllerTests : MultiplayerTest
    {
        public MatchmakingMatchControllerTests()
        {
            AppSettings.MatchmakingRoomSize = 2;
            AppSettings.MatchmakingRoomRounds = 2;
            AppSettings.MatchmakingRoomAllowSkip = true;

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(roomId => InitialiseRoom(roomId, 10))
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.matchmaking,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!),
                    });

            Database.Setup(db => db.GetMatchmakingUserStatsAsync(It.IsAny<int>(), It.IsAny<int>()))
                    .Returns<int, int>((userId, rulesetId) => Task.FromResult<matchmaking_user_stats?>(new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        ruleset_id = (ushort)rulesetId
                    }));
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

            // Check that the room is waiting for users.
            await verifyStage(MatchmakingStage.WaitingForClientsJoin);

            // Check the user received the matchmaking state.
            Receiver.Verify(u => u.MatchRoomStateChanged(It.Is<MatchmakingRoomState>(s => s.Stage == MatchmakingStage.WaitingForClientsJoin)), Times.AtLeastOnce());
            Receiver.Invocations.Clear();

            // Join the second user.
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

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
                    selectedPlaylistItems = room.Item!.Playlist.Where(item => !item.Expired).Take(2).Select(item => item.ID).ToArray();
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
                await Hub.ChangeState(MultiplayerUserState.Ready);
                await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Ready);
                await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

                // Check that the room continued to the next stage because all players downloaded the beatmap.
                await verifyStage(MatchmakingStage.GameplayWarmupTime);

                // Begin gameplay.
                await gotoNextStage();
                await verifyStage(MatchmakingStage.Gameplay);

                // Check that a request to load gameplay was started.
                Receiver.Verify(u => u.LoadRequested(), Times.Once);

                // Start gameplay for both users.
                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

                // End gameplay for both users
                SetUserContext(ContextUser);
                await Hub.AbortGameplay();
                SetUserContext(ContextUser2);
                await Hub.AbortGameplay();

                // Check that the room continued to show the results after gameplay.
                await verifyStage(MatchmakingStage.ResultsDisplaying);

                // Check that the standings were updated.
                using (var room = await Rooms.GetForUse(ROOM_ID))
                {
                    Assert.Equal(15 * i, ((MatchmakingRoomState)room.Item!.MatchState!).Users[USER_ID].Points);
                    Assert.Equal(1, ((MatchmakingRoomState)room.Item!.MatchState!).Users[USER_ID].Placement);

                    Assert.Equal(12 * i, ((MatchmakingRoomState)room.Item!.MatchState!).Users[USER_ID_2].Points);
                    Assert.Equal(2, ((MatchmakingRoomState)room.Item!.MatchState!).Users[USER_ID_2].Placement);
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
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

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
    }
}
