// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Events;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    [NonController]
    public class MatchmakingMatchController : IMatchController
    {
        /// <summary>
        /// Duration users are given to enter the room before it automatically starts.
        /// This is not expected to run to completion.
        /// </summary>
        private const int stage_waiting_for_clients_join_time = 60;

        /// <summary>
        /// Duration users are given to view standings at the round start screen.
        /// </summary>
        private const int stage_round_start_time = 15;

        /// <summary>
        /// Duration users are given to pick their beatmap.
        /// </summary>
        private const int stage_user_picks_time = 40;

        /// <summary>
        /// Duration for the user beatmap selection stage when all users have picked a beatmap.
        /// </summary>
        private const int stage_user_picks_time_fast = 5;

        /// <summary>
        /// Duration before the beatmap is revealed to users (should approximate client animation time).
        /// </summary>
        private const int stage_select_beatmap_time = 7;

        /// <summary>
        /// Duration users are given to download the beatmap before they're excluded from the match.
        /// </summary>
        private const int stage_prepare_beatmap_time = 120;

        /// <summary>
        /// Duration users are given to prepare for the match to start.
        /// </summary>
        private const int stage_prepare_gameplay_time = 5;

        /// <summary>
        /// Unused.
        /// </summary>
        private const int stage_gameplay_time = 0;

        /// <summary>
        /// Duration users are given to preview the results of a round before they're forced back to the match.
        /// </summary>
        private const int stage_round_end_time = 10;

        /// <summary>
        /// Duration after the match concludes before the room is closed.
        /// </summary>
        private const int stage_room_end_time = 120;

        /// <summary>
        /// The room size.
        /// </summary>
        private static readonly int room_size = AppSettings.MatchmakingRoomSize;

        /// <summary>
        /// The total number of rounds.
        /// </summary>
        private static readonly int total_rounds = AppSettings.MatchmakingRoomRounds;

        /// <summary>
        /// The number of points awarded for each placement position (index 0 = #1, index 7 = #8).
        /// </summary>
        private static readonly int[] placement_points = [15, 12, 10, 8, 6, 4, 2, 1];

        public MultiplayerPlaylistItem CurrentItem => room.CurrentPlaylistItem;

        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly MatchmakingRoomState state;
        private readonly Dictionary<int, long> userPicks = new Dictionary<int, long>();
        private readonly int rulesetId;

        private int joinedUserCount;

        public MatchmakingMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
        {
            this.room = room;
            this.hub = hub;
            this.dbFactory = dbFactory;

            room.MatchState = state = new MatchmakingRoomState();
            room.Settings.PlaylistItemId = room.Playlist[Random.Shared.Next(0, room.Playlist.Count)].ID;

            // Todo: This should be retrieved from the room creation parameters instead.
            rulesetId = CurrentItem.RulesetID;
        }

        public async Task Initialise()
        {
            await hub.NotifyMatchRoomStateChanged(room);
            await startCountdown(TimeSpan.FromSeconds(stage_waiting_for_clients_join_time), stageRoundWarmupTime);
        }

        public Task HandleSettingsChanged()
        {
            return Task.CompletedTask;
        }

        public async Task HandleGameplayCompleted()
        {
            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(room.RoomID, CurrentItem.ID);
                room.Playlist[room.Playlist.IndexOf(CurrentItem)] = (await db.GetPlaylistItemAsync(room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem();
                await hub.NotifyPlaylistItemChanged(room, CurrentItem, true);
            }

            await stageResultsDisplaying();
        }

        public async Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            switch (request)
            {
                case MatchmakingAvatarActionRequest avatarAction:
                    await hub.NotifyNewMatchEvent(room, new MatchmakingAvatarActionEvent
                    {
                        UserId = user.UserID,
                        Action = avatarAction.Action
                    });
                    break;
            }
        }

        public async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            switch (state.Stage)
            {
                case MatchmakingStage.WaitingForClientsJoin:
                    if (++joinedUserCount >= room_size)
                        await stageRoundWarmupTime(room);
                    break;
            }
        }

        public async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            await updateStageFromUserStateChange();
        }

        public Task AddPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public Task EditPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public Task RemovePlaylistItem(long playlistItemId, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            await updateStageFromUserStateChange();
        }

        public void SkipToNextStage(out Task countdownTask)
        {
            if (!AppSettings.MatchmakingRoomAllowSkip)
                throw new InvalidStateException("Skipping matchmaking rounds is not allowed.");

            countdownTask = room.SkipToEndOfCountdown(room.FindCountdownOfType<MatchmakingStageCountdown>());
        }

        public async Task ToggleSelectionAsync(MultiplayerRoomUser user, long playlistItemId)
        {
            if (state.Stage != MatchmakingStage.UserBeatmapSelect)
                return;

            MultiplayerPlaylistItem? item = room.Playlist.SingleOrDefault(item => item.ID == playlistItemId);

            if (item == null)
                throw new InvalidStateException("Selected playlist item is not part of the room!");

            if (item.Expired)
                throw new InvalidStateException("Selected playlist item is expired!");

            if (userPicks.TryGetValue(user.UserID, out long existingPick))
            {
                if (existingPick == playlistItemId)
                    return;

                await hub.NotifyMatchmakingItemDeselected(room, user.UserID, existingPick);
            }

            userPicks[user.UserID] = playlistItemId;
            await hub.NotifyMatchmakingItemSelected(room, user.UserID, playlistItemId);

            // Fast-forward the countdown if all players have made a selection.
            if (userPicks.Count == room.Users.Count)
            {
                MatchmakingStageCountdown? countdown = room.FindCountdownOfType<MatchmakingStageCountdown>();
                Debug.Assert(countdown != null);

                if (room.GetCountdownRemainingTime(countdown) <= TimeSpan.FromSeconds(stage_user_picks_time_fast))
                    return;

                await room.StopCountdown(countdown);
                await startCountdown(TimeSpan.FromSeconds(stage_user_picks_time_fast), stageServerBeatmapFinalised);
            }
        }

        private async Task stageRoundWarmupTime(ServerMultiplayerRoom _)
        {
            state.CurrentRound++;

            await changeStage(MatchmakingStage.RoundWarmupTime);
            await returnUsersToRoom(room);
            await startCountdown(TimeSpan.FromSeconds(stage_round_start_time), stageUserBeatmapSelect);
        }

        private async Task stageUserBeatmapSelect(ServerMultiplayerRoom _)
        {
            userPicks.Clear();

            await changeStage(MatchmakingStage.UserBeatmapSelect);
            await startCountdown(TimeSpan.FromSeconds(stage_user_picks_time), stageServerBeatmapFinalised);
        }

        private async Task stageServerBeatmapFinalised(ServerMultiplayerRoom _)
        {
            long[] pickIds = userPicks.Values.ToArray();

            // When there are no picks, select ONE beatmap at random to be played.
            if (pickIds.Length == 0)
            {
                long[] availableItems = room.Playlist.Where(item => !item.Expired && !pickIds.Contains(item.ID)).Select(i => i.ID).ToArray();
                pickIds = Random.Shared.GetItems(availableItems, 1);
            }

            state.CandidateItems = pickIds.Distinct().ToArray();
            state.CandidateItem = pickIds[Random.Shared.Next(0, pickIds.Length)];

            await changeStage(MatchmakingStage.ServerBeatmapFinalised);
            await startCountdown(TimeSpan.FromSeconds(stage_select_beatmap_time), stageWaitingForClientsBeatmapDownload);
        }

        private async Task stageWaitingForClientsBeatmapDownload(ServerMultiplayerRoom _)
        {
            room.Settings.PlaylistItemId = state.CandidateItem;
            await hub.NotifySettingsChanged(room, true);

            await changeStage(MatchmakingStage.WaitingForClientsBeatmapDownload);
            await startCountdown(TimeSpan.FromSeconds(stage_prepare_beatmap_time), _ => anyUsersReady() ? stageGameplayWarmupTime(room) : stageWaitingForClientsBeatmapDownload(room));
        }

        private async Task stageGameplayWarmupTime(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingStage.GameplayWarmupTime);
            await startCountdown(TimeSpan.FromSeconds(stage_prepare_gameplay_time), stageGameplay);
        }

        private async Task stageGameplay(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingStage.Gameplay);
            await startCountdown(TimeSpan.FromSeconds(stage_gameplay_time), hub.StartMatch);
        }

        private async Task stageResultsDisplaying()
        {
            Dictionary<int, SoloScore> scores = new Dictionary<int, SoloScore>();

            using (var db = dbFactory.GetInstance())
            {
                foreach (var score in await db.GetAllScoresForPlaylistItem(CurrentItem.ID))
                    scores[(int)score.user_id] = score;
            }

            state.RecordScores(scores.Values.Select(s => s.ToScoreInfo()).ToArray(), placement_points);

            if (state.CurrentRound == total_rounds)
                await updateUserStats();

            await changeStage(MatchmakingStage.ResultsDisplaying);

            if (state.CurrentRound == total_rounds)
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoomEnd);
            else
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoundWarmupTime);
        }

        private async Task stageRoomEnd(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingStage.Ended);
            await startCountdown(TimeSpan.FromSeconds(stage_room_end_time), _ => Task.CompletedTask);
        }

        private async Task updateUserStats()
        {
            using (var db = dbFactory.GetInstance())
            {
                List<matchmaking_user_stats> userStats = [];
                List<EloPlayer> eloStandings = [];

                foreach (var user in state.Users.OrderBy(u => u.Placement))
                {
                    matchmaking_user_stats stats = await db.GetMatchmakingUserStatsAsync(user.UserId, rulesetId) ?? new matchmaking_user_stats
                    {
                        user_id = (uint)user.UserId,
                        ruleset_id = (ushort)rulesetId
                    };

                    if (user.Placement == 1)
                        stats.first_placements++;
                    stats.total_points += (uint)user.Points;

                    userStats.Add(stats);
                    eloStandings.Add(stats.EloData);
                }

                EloContest eloContest = new EloContest(DateTimeOffset.Now, eloStandings.ToArray());
                EloSystem eloSystem = new EloSystem
                {
                    MaxHistory = 10
                };

                eloSystem.RecordContest(eloContest);

                foreach (var stats in userStats)
                    await db.UpdateMatchmakingUserStatsAsync(stats);
            }
        }

        private async Task returnUsersToRoom(ServerMultiplayerRoom _)
        {
            foreach (var user in room.Users.Where(u => u.State != MultiplayerUserState.Idle))
                await changeUserState(user, MultiplayerUserState.Idle);
        }

        private async Task changeUserState(MultiplayerRoomUser user, MultiplayerUserState newState)
        {
            await hub.ChangeAndBroadcastUserState(room, user, newState);
            await hub.UpdateRoomStateIfRequired(room);
        }

        private async Task changeStage(MatchmakingStage stage)
        {
            state.Stage = stage;
            await hub.NotifyMatchRoomStateChanged(room);
        }

        private async Task startCountdown(TimeSpan duration, Func<ServerMultiplayerRoom, Task> continuation)
        {
            await room.StartCountdown(new MatchmakingStageCountdown
            {
                Stage = state.Stage,
                TimeRemaining = duration
            }, continuation);
        }

        private async Task updateStageFromUserStateChange()
        {
            switch (state.Stage)
            {
                case MatchmakingStage.WaitingForClientsBeatmapDownload:
                    if (allUsersReady())
                        await stageGameplayWarmupTime(room);
                    break;
            }
        }

        private bool allUsersReady()
        {
            return room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        private bool anyUsersReady()
        {
            return room.Users.Any(u => u.State == MultiplayerUserState.Ready);
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };
    }
}
