// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Events;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    [NonController]
    public class MatchmakingMatchController : IMatchController, IMatchmakingMatchController
    {
        /// <summary>
        /// Duration users are given to enter the room before it automatically starts.
        /// This is not expected to run to completion.
        /// </summary>
        private const int stage_waiting_for_clients_join_time = 60;

        /// <summary>
        /// Duration before the match starts the first round.
        /// </summary>
        private const int stage_round_start_time_first = 15;

        /// <summary>
        /// Duration users are given to view standings at the round start screen.
        /// </summary>
        private const int stage_round_start_time = 5;

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
        private const int stage_select_beatmap_time_single_item = 3;

        /// <summary>
        /// Duration before the beatmap is revealed to users (should approximate client animation time).
        /// </summary>
        private const int stage_select_beatmap_time_multiple_items = 7;

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
        /// The total number of rounds.
        /// </summary>
        private static int totalRounds => AppSettings.MatchmakingRoomRounds;

        /// <summary>
        /// The number of points awarded for each placement position (index 0 = #1, index 7 = #8).
        /// </summary>
        private static readonly int[] placement_points = [15, 12, 10, 8, 6, 4, 2, 1];

        public MultiplayerPlaylistItem CurrentItem => room.Playlist.Single(item => item.ID == room.Settings.PlaylistItemId);

        private readonly ServerMultiplayerRoom room;
        private readonly IDatabaseFactory dbFactory;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly MatchmakingRoomState state;
        private readonly Dictionary<int, long> userPicks = new Dictionary<int, long>();

        private uint poolId;
        private int joinedUserCount;
        private bool anyPlayerQuit;
        private bool statsUpdatePending = true;

        public MatchmakingMatchController(ServerMultiplayerRoom room, IDatabaseFactory dbFactory, MultiplayerEventDispatcher eventDispatcher)
        {
            this.room = room;
            this.dbFactory = dbFactory;
            this.eventDispatcher = eventDispatcher;

            room.MatchState = state = new MatchmakingRoomState();
        }

        public async Task Initialise()
        {
            await eventDispatcher.PostMatchRoomStateChangedAsync(room.RoomID, room.MatchState);
            await startCountdown(TimeSpan.FromSeconds(stage_waiting_for_clients_join_time), stageRoundWarmupTime);
        }

        public async Task Initialise(uint poolId, MatchmakingQueueUser[] users, MatchmakingBeatmapSelector beatmapSelector)
        {
            this.poolId = poolId;

            using (var db = dbFactory.GetInstance())
            {
                foreach (var beatmap in beatmapSelector.GetAppropriateBeatmaps(users.Select(u => u.Rating).ToArray()))
                {
                    MultiplayerPlaylistItem item = beatmap.ToPlaylistItem();
                    item.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, item));
                    room.Playlist.Add(item);
                }
            }

            room.Settings.PlaylistItemId = room.Playlist[Random.Shared.Next(0, room.Playlist.Count)].ID;

            foreach (var user in users)
                state.Users.GetOrAdd(user.UserId);
        }

        public Task<bool> UserCanJoin(int userId)
            => Task.FromResult(state.Users.UserDictionary.ContainsKey(userId));

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
                await room.HandlePlaylistItemChanged(CurrentItem, true);
            }

            // Collect all scores from the database.
            List<SoloScore> scores = [];
            using (var db = dbFactory.GetInstance())
                scores.AddRange(await db.GetAllScoresForPlaylistItem(CurrentItem.ID));

            // Add dummy scores for all users that did not play the map.
            foreach ((int userId, _) in state.Users.UserDictionary)
            {
                if (scores.All(s => s.user_id != userId))
                    scores.Add(new SoloScore { user_id = (uint)userId });
            }

            state.RecordScores(scores.Select(s => s.ToScoreInfo()).ToArray(), placement_points);

            await stageResultsDisplaying();
        }

        public async Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            switch (request)
            {
                case MatchmakingAvatarActionRequest avatarAction:
                    await eventDispatcher.PostMatchEventAsync(room.RoomID, new MatchmakingAvatarActionEvent
                    {
                        UserId = user.UserID,
                        Action = avatarAction.Action
                    });
                    break;

                case MatchmakingCursorPositionRequest cursorPosition:
                    await hub.NotifyNewMatchEvent(room, new MatchmakingCursorPositionEvent
                    {
                        UserId = user.UserID,
                        X = cursorPosition.X,
                        Y = cursorPosition.Y,
                    });
                    break;
            }
        }

        public async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            switch (state.Stage)
            {
                case MatchmakingStage.WaitingForClientsJoin:
                    await eventDispatcher.PostPlayerJoinedMatchmakingRoomAsync(room.RoomID, user.UserID);

                    if (++joinedUserCount >= state.Users.Count)
                        await stageRoundWarmupTime(room);
                    break;
            }
        }

        public async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            anyPlayerQuit = true;

            // Mark users that leave the match early as having abandoned the match.
            if (hasGameplayRoundsRemaining())
            {
                state.Users.GetOrAdd(user.UserID).AbandonedAt = DateTimeOffset.UtcNow;
                state.RecordScores([], placement_points); // Empty update to adjust placements.
                await eventDispatcher.PostMatchRoomStateChangedAsync(room.RoomID, room.MatchState);
            }

            // Attempt to conclude the match in advance so users don't have to keep playing rounds by themselves.
            if (canConcludeMatch())
            {
                await stageRoomEnd(room);
                return;
            }

            userPicks.Remove(user.UserID);
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

            if (playlistItemId != -1)
            {
                MultiplayerPlaylistItem? item = room.Playlist.SingleOrDefault(item => item.ID == playlistItemId);

                if (item == null)
                    throw new InvalidStateException("Selected playlist item is not part of the room!");

                if (item.Expired)
                    throw new InvalidStateException("Selected playlist item is expired!");
            }

            if (userPicks.TryGetValue(user.UserID, out long existingPick))
            {
                if (existingPick == playlistItemId)
                    return;

                await eventDispatcher.PostPlayerDeselectedBeatmapAsync(room.RoomID, user.UserID, existingPick);
            }

            userPicks[user.UserID] = playlistItemId;
            await eventDispatcher.PostPlayerSelectedBeatmapAsync(room.RoomID, user.UserID, playlistItemId);

            await checkCanFastForwardBeatmapSelection();
        }

        private async Task checkCanFastForwardBeatmapSelection()
        {
            Debug.Assert(state.Stage == MatchmakingStage.UserBeatmapSelect);

            // Fast-forward the countdown if all players have made a selection.
            if (userPicks.Count != room.Users.Count)
                return;

            MatchmakingStageCountdown? countdown = room.FindCountdownOfType<MatchmakingStageCountdown>();
            Debug.Assert(countdown != null);

            if (room.GetCountdownRemainingTime(countdown) <= TimeSpan.FromSeconds(stage_user_picks_time_fast))
                return;

            await room.StopCountdown(countdown);
            await startCountdown(TimeSpan.FromSeconds(stage_user_picks_time_fast), stageServerBeatmapFinalised);
        }

        private async Task stageRoundWarmupTime(ServerMultiplayerRoom _)
        {
            state.CurrentRound++;

            await changeStage(MatchmakingStage.RoundWarmupTime);
            await returnUsersToRoom(room);
            await startCountdown(
                state.CurrentRound == 1
                    ? TimeSpan.FromSeconds(stage_round_start_time_first)
                    : TimeSpan.FromSeconds(stage_round_start_time),
                stageUserBeatmapSelect);
        }

        private async Task stageUserBeatmapSelect(ServerMultiplayerRoom _)
        {
            userPicks.Clear();

            await changeStage(MatchmakingStage.UserBeatmapSelect);
            await startCountdown(TimeSpan.FromSeconds(stage_user_picks_time), stageServerBeatmapFinalised);
        }

        private async Task stageServerBeatmapFinalised(ServerMultiplayerRoom _)
        {
            foreach ((int userId, long playlistItemId) in userPicks)
                await eventDispatcher.PostPlayerBeatmapPickFinalised(room.RoomID, userId, playlistItemId);

            long[] pickIds = userPicks.Values.ToArray();

            // When there are no picks, select ONE beatmap at random to be played.
            if (pickIds.Length == 0)
                pickIds = Random.Shared.GetItems(room.Playlist.Where(item => !item.Expired).Select(i => i.ID).ToArray(), 1);

            state.CandidateItems = pickIds.Distinct().ToArray();
            state.CandidateItem = pickIds[Random.Shared.Next(0, pickIds.Length)];
            state.GameplayItem = state.CandidateItem == -1
                ? Random.Shared.GetItems(room.Playlist.Where(item => !item.Expired).Select(i => i.ID).ToArray(), 1)[0]
                : state.CandidateItem;

            await changeStage(MatchmakingStage.ServerBeatmapFinalised);
            await startCountdown(state.CandidateItems.Length == 1
                    ? TimeSpan.FromSeconds(stage_select_beatmap_time_single_item)
                    : TimeSpan.FromSeconds(stage_select_beatmap_time_multiple_items),
                stageWaitingForClientsBeatmapDownload);
        }

        private async Task stageWaitingForClientsBeatmapDownload(ServerMultiplayerRoom _)
        {
            // The settings playlist item controls various components by the client such as download tracking,
            // so it is set as late as possible to not inedvertently reveal it before animations are complete.
            room.Settings.PlaylistItemId = state.GameplayItem;
            await room.HandleSettingsChanged(true);

            await eventDispatcher.PostFinalBeatmapSelectedAsync(room.RoomID, room.Settings.PlaylistItemId);

            await changeStage(MatchmakingStage.WaitingForClientsBeatmapDownload);
            await tryAdvanceStage();

            async Task tryAdvanceStage()
                => await startCountdown(TimeSpan.FromSeconds(stage_prepare_beatmap_time), _ => hasEnoughUsersForGameplay() ? stageGameplayWarmupTime(room) : tryAdvanceStage());
        }

        private async Task stageGameplayWarmupTime(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingStage.GameplayWarmupTime);
            await startCountdown(TimeSpan.FromSeconds(stage_prepare_gameplay_time), stageGameplay);
        }

        private async Task stageGameplay(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingStage.Gameplay);
            await startCountdown(TimeSpan.FromSeconds(stage_gameplay_time), ServerMultiplayerRoom.StartMatch);
        }

        private async Task stageResultsDisplaying()
        {
            await changeStage(MatchmakingStage.ResultsDisplaying);

            if (canConcludeMatch())
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoomEnd);
            else
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoundWarmupTime);
        }

        private async Task stageRoomEnd(ServerMultiplayerRoom _)
        {
            await updateUserStats();

            await changeStage(MatchmakingStage.Ended);
            await startCountdown(TimeSpan.FromSeconds(stage_room_end_time), _ => Task.CompletedTask);
        }

        private async Task updateUserStats()
        {
            if (!statsUpdatePending)
                return;

            using (var db = dbFactory.GetInstance())
            {
                PlackettLuce model = new PlackettLuce
                {
                    Mu = 1500,
                    Sigma = 350,
                    Beta = 175,
                    Tau = 3.5
                };

                List<matchmaking_user_stats> stats = [];
                List<ITeam> teams = [];
                List<double> ranks = [];
                int rankIndex = -1;

                foreach (MatchmakingUser user in state.Users.Where(u => u.Points > 0).OrderBy(u => u.Placement))
                {
                    matchmaking_user_stats userStats = await db.GetMatchmakingUserStatsAsync(user.UserId, poolId) ?? new matchmaking_user_stats
                    {
                        user_id = (uint)user.UserId,
                        pool_id = poolId
                    };

                    if (user.Placement == 1)
                        userStats.first_placements++;
                    userStats.total_points += (uint)user.Points;

                    stats.Add(userStats);
                    teams.Add(new Team { Players = [model.Rating(userStats.EloData.Rating.Mu, userStats.EloData.Rating.Sig)] });
                    ranks.Add(++rankIndex);
                }

                ITeam[] newRatings = model.Rate(teams, ranks).ToArray();

                for (int i = 0; i < stats.Count; i++)
                {
                    stats[i].EloData.ContestCount++;
                    stats[i].EloData.Rating = new EloRating(newRatings[i].Players.Single().Mu, newRatings[i].Players.Single().Sigma);
                    await db.UpdateMatchmakingUserStatsAsync(stats[i]);
                }
            }

            statsUpdatePending = false;
        }

        private async Task returnUsersToRoom(ServerMultiplayerRoom _)
        {
            foreach (var user in room.Users.Where(u => u.State != MultiplayerUserState.Idle))
                await changeUserState(user, MultiplayerUserState.Idle);
        }

        private async Task changeUserState(MultiplayerRoomUser user, MultiplayerUserState newState)
        {
            await room.ChangeAndBroadcastUserState(user, newState);
            await room.UpdateRoomStateIfRequired();
        }

        private async Task changeStage(MatchmakingStage stage)
        {
            state.Stage = stage;
            await eventDispatcher.PostMatchRoomStateChangedAsync(room.RoomID, room.MatchState);
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

                case MatchmakingStage.UserBeatmapSelect:
                    await checkCanFastForwardBeatmapSelection();
                    break;
            }
        }

        private bool allUsersReady()
        {
            return room.Users.All(u => u.IsReadyForGameplay());
        }

        private bool hasEnoughUsersForGameplay()
        {
            return
                // Special case for testing in solo play.
                (room.Users.Count == 1 && allUsersReady())
                // Otherwise, always require at least two ready users.
                || room.Users.Count(u => u.IsReadyForGameplay()) >= 2;
        }

        /// <summary>
        /// Whether the match can be transitioned into a <see cref="MatchmakingStage.Ended">concluded</see> state,
        /// provided that it is no longer necessary for any users to keep playing.
        /// </summary>
        private bool canConcludeMatch()
        {
            return
                // Gameplay has concluded.
                !hasGameplayRoundsRemaining()
                // Only a single player remains, that is no longer in gameplay.
                || (anyPlayerQuit && room.Users.Count == 1 && state.Stage != MatchmakingStage.Gameplay);
        }

        /// <summary>
        /// Whether there are still gameplay rounds to be played.
        /// If <c>true</c>, users leaving the match will receive an abandon penalty.
        /// </summary>
        private bool hasGameplayRoundsRemaining()
        {
            return
                // The room has not been terminated early.
                state.Stage != MatchmakingStage.Ended
                // Users are remaining in the room.
                && room.Users.Count > 0
                // The room has not yet run through to its natural conclusion.
                && (state.CurrentRound < totalRounds || state.Stage <= MatchmakingStage.Gameplay)
                // In best-of mode, a winner has not been decided yet.
                && !hasAnyBestOfWinner();
        }

        /// <summary>
        /// Whether any user has won a head-to-head matchup using the best-of win condition.
        /// </summary>
        private bool hasAnyBestOfWinner()
        {
            if (state.Users.Count != 2 || !AppSettings.MatchmakingHeadToHeadIsBestOf)
                return false;

            int requiredWins = totalRounds / 2 + 1;
            return state.Users.Any(u => u.Rounds.Count(r => r.Placement == 1) == requiredWins);
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };
    }
}
