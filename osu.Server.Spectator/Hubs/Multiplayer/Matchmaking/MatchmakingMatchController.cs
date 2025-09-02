// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MatchmakingMatchController : IMatchController
    {
        /// <summary>
        /// Duration users are given to view standings at the round start screen.
        /// </summary>
        private const int stage_round_start_time = 10;

        /// <summary>
        /// Duration users are given to pick their beatmap.
        /// </summary>
        private const int stage_user_picks_time = 20;

        /// <summary>
        /// Duration before the beatmap is revealed to users (should approximate client animation time).
        /// </summary>
        private const int stage_select_beatmap_time = 5;

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
        /// The size of matchmaking rooms.
        /// </summary>
        public const int MATCHMAKING_ROOM_SIZE = 8;

        /// <summary>
        /// The beatmaps that form the playlist for each ruleset.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, int[]> BEATMAP_IDS = new Dictionary<int, int[]>
        {
            { 0, [259, 830459, 841629, 853336, 882017] },
            { 1, [819935, 819935, 819935, 819935, 819935] },
            { 2, [527141, 527141, 527141, 527141, 527141] },
            { 3, [710980, 710980, 710980, 710980, 710980] },
        };

        private const int total_rounds = 5;

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

        public MatchmakingMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
        {
            this.room = room;
            this.hub = hub;
            this.dbFactory = dbFactory;

            room.MatchState = state = new MatchmakingRoomState();
            room.Settings.PlaylistItemId = room.Playlist[0].ID;
        }

        public async Task Initialise()
        {
            await hub.NotifyMatchRoomStateChanged(room);
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

        public Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        public async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            switch (state.Stage)
            {
                case MatchmakingStage.WaitingForClientsJoin:
                    if (room.Users.Count == MATCHMAKING_ROOM_SIZE)
                        await stageRoundWarmupTime(room);
                    break;
            }
        }

        public Task HandleUserLeft(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
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
            switch (state.Stage)
            {
                case MatchmakingStage.WaitingForClientsBeatmapDownload:
                    if (allUsersReady())
                        await stageGameplayWarmupTime(room);
                    break;
            }
        }

        public async Task SkipToNextRound()
        {
            _ = room.SkipToEndOfCountdown(room.FindCountdownOfType<MatchmakingStageCountdown>());
            await Task.CompletedTask;
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
            int remainderPickCount = room.Users.Count - pickIds.Length;

            if (remainderPickCount > 0)
            {
                long[] availablePicks = room.Playlist.Where(item => !item.Expired && !pickIds.Contains(item.ID)).Select(i => i.ID).ToArray();
                Random.Shared.Shuffle(availablePicks);
                pickIds = pickIds.Concat(availablePicks.Take(remainderPickCount)).ToArray();
            }

            state.CandidateItems = pickIds;
            state.CandidateItem = pickIds[Random.Shared.Next(0, pickIds.Length)];

            await changeStage(MatchmakingStage.ServerBeatmapFinalised);
            await startCountdown(TimeSpan.FromSeconds(stage_select_beatmap_time), stageWaitingForClientsBeatmapDownload);
        }

        private async Task stageWaitingForClientsBeatmapDownload(ServerMultiplayerRoom _)
        {
            long lastPlaylistItem = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = state.CandidateItem;
            await hub.NotifySettingsChanged(room, lastPlaylistItem != room.Settings.PlaylistItemId);

            if (allUsersReady())
                await stageGameplayWarmupTime(room);
            else
            {
                await changeStage(MatchmakingStage.WaitingForClientsBeatmapDownload);
                await startCountdown(TimeSpan.FromSeconds(stage_prepare_beatmap_time), _ => anyUsersReady() ? stageGameplayWarmupTime(room) : stageWaitingForClientsBeatmapDownload(room));
            }
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

            await changeStage(MatchmakingStage.ResultsDisplaying);

            if (state.CurrentRound == total_rounds)
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoomEnd);
            else
                await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoundWarmupTime);
        }

        private async Task stageRoomEnd(ServerMultiplayerRoom _)
        {
            MatchmakingUser? firstPlaceUser = state.Users.FirstOrDefault(u => u.Placement == 1);

            // Can be null in the case none of the users played a map.
            if (firstPlaceUser != null)
            {
                using (var db = dbFactory.GetInstance())
                    await db.IncrementMatchmakingFirstPlacementsAsync(firstPlaceUser.UserId);
            }

            await changeStage(MatchmakingStage.Ended);
            await startCountdown(TimeSpan.FromSeconds(stage_room_end_time), _ => Task.CompletedTask);
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

        private bool allUsersReady()
        {
            return room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        private bool anyUsersReady()
        {
            return room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };
    }
}
