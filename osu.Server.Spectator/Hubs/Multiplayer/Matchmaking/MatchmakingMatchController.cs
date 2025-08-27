// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
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
        private const int stage_round_start_time = 5;
        private const int stage_user_picks_time = 5;
        private const int stage_select_beatmap_time = 5;
        private const int stage_prepare_beatmap_time = 120;
        private const int stage_prepare_gameplay_time = 5;
        private const int stage_gameplay_time = 0;
        private const int stage_round_end_quick_time = 5;
        private const int stage_round_end_time = 5;
        private const int stage_room_end_time = 10;

        /// <summary>
        /// The size of matchmaking rooms.
        /// </summary>
        public const int MATCHMAKING_ROOM_SIZE = 1;

        /// <summary>
        /// The beatmaps that form the playlist.
        /// </summary>
        public static readonly int[] BEATMAP_IDS =
        [
            186036, 221862, 222774, 222775, 384926, 412968, 415098, 415099, 419747, 424260, 436442, 437721, 438096, 440820, 441920, 454385, 492862, 497679, 497680, 526342, 526935, 550385, 554956,
            554957, 558078, 609843, 613823, 614095, 618865, 626245, 626495, 628637, 628995, 630747, 632127, 634101, 635625, 641039, 642687, 642689, 644573, 644837, 645765, 645783, 647026, 649186,
            649545, 650188, 651348, 654922, 657476, 661216, 665240, 666332, 675157, 695694, 700000, 705760, 710881, 713594, 714221, 718670, 720172, 723308, 727447, 730770, 733943, 735990, 737284,
            737818, 738062, 739825, 739826, 743069, 743192, 743395, 743469, 746533, 746545, 746840, 749857, 750961, 751598, 751599, 755258, 755932, 756249, 757970, 758383, 759638, 759911, 761722,
            762086, 762511, 762762, 763308, 764193, 764271, 765873, 766080, 767203, 767252, 767253, 767988, 769395, 770727, 772067, 772987, 773155, 773410, 775561, 778792, 780119, 780140, 780863,
            780864, 780865, 781495, 781872, 781873, 783305, 784601, 785409, 787961, 788245, 788676, 789771, 789820
        ];

        private const int total_rounds = 4;

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

                // Add a non-expired duplicate of the current item back to the room.
                MultiplayerPlaylistItem newItem = CurrentItem.Clone();
                newItem.Expired = false;
                newItem.PlayedAt = null;
                newItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(room.RoomID, newItem));
                room.Playlist.Add(newItem);
                await hub.NotifyPlaylistItemAdded(room, newItem);
            }

            await stageRoundEnd();
        }

        public Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        public async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            switch (state.RoomStatus)
            {
                case MatchmakingRoomStatus.RoomStart:
                    if (room.Users.Count == MATCHMAKING_ROOM_SIZE)
                        await stageRoundStart(room);
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
            switch (state.RoomStatus)
            {
                case MatchmakingRoomStatus.PrepareBeatmap:
                    if (allUsersReady())
                        await stagePrepareGameplay(room);
                    break;

                case MatchmakingRoomStatus.RoundEnd:
                    if (allUsersIdle())
                        await stageRoundStart(room);
                    break;
            }
        }

        public async Task SkipToNextRound()
        {
            _ = room.SkipToEndOfCountdown(room.FindCountdownOfType<MatchmakingStatusCountdown>());
            await Task.CompletedTask;
        }

        public async Task ToggleSelectionAsync(MultiplayerRoomUser user, long playlistItemId)
        {
            if (state.RoomStatus != MatchmakingRoomStatus.UserPicks)
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

                await hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingItemDeselected), user.UserID, existingPick);
            }

            userPicks[user.UserID] = playlistItemId;

            await hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingItemSelected), user.UserID, playlistItemId);
        }

        private async Task stageRoundStart(ServerMultiplayerRoom _)
        {
            state.Round++;

            await changeStage(MatchmakingRoomStatus.RoundStart);
            await returnUsersToRoom(room);
            await startCountdown(TimeSpan.FromSeconds(stage_round_start_time), stageUserPicks);
        }

        private async Task stageUserPicks(ServerMultiplayerRoom _)
        {
            userPicks.Clear();

            await changeStage(MatchmakingRoomStatus.UserPicks);
            await startCountdown(TimeSpan.FromSeconds(stage_user_picks_time), stageSelectBeatmap);
        }

        private async Task stageSelectBeatmap(ServerMultiplayerRoom _)
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

            await changeStage(MatchmakingRoomStatus.SelectBeatmap);
            await startCountdown(TimeSpan.FromSeconds(stage_select_beatmap_time), stagePrepareBeatmap);
        }

        private async Task stagePrepareBeatmap(ServerMultiplayerRoom _)
        {
            long lastPlaylistItem = room.Settings.PlaylistItemId;
            room.Settings.PlaylistItemId = state.CandidateItem;
            await hub.NotifySettingsChanged(room, lastPlaylistItem != room.Settings.PlaylistItemId);

            if (allUsersReady())
                await stagePrepareGameplay(room);
            else
            {
                await changeStage(MatchmakingRoomStatus.PrepareBeatmap);
                await startCountdown(TimeSpan.FromSeconds(stage_prepare_beatmap_time), _ => anyUsersReady() ? stagePrepareGameplay(room) : stagePrepareBeatmap(room));
            }
        }

        private async Task stagePrepareGameplay(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingRoomStatus.PrepareGameplay);
            await startCountdown(TimeSpan.FromSeconds(stage_prepare_gameplay_time), stageGameplay);
        }

        private async Task stageGameplay(ServerMultiplayerRoom _)
        {
            await changeStage(MatchmakingRoomStatus.Gameplay);
            await startCountdown(TimeSpan.FromSeconds(stage_gameplay_time), hub.StartMatch);
        }

        private async Task stageRoundEnd()
        {
            Dictionary<int, SoloScore> scores = new Dictionary<int, SoloScore>();

            using (var db = dbFactory.GetInstance())
            {
                foreach (var score in await db.GetAllScoresForPlaylistItem(CurrentItem.ID))
                    scores[(int)score.user_id] = score;
            }

            state.SetScores(scores.Values.Select(s => s.ToScoreInfo()).ToArray());

            if (state.Round == total_rounds)
            {
                await updateUserStats();
                await changeStage(MatchmakingRoomStatus.RoomEnd);
                await startCountdown(TimeSpan.FromSeconds(stage_room_end_time), hub.CloseRoom);
            }
            else
            {
                await changeStage(MatchmakingRoomStatus.RoundEnd);

                if (allUsersIdle())
                    await startCountdown(TimeSpan.FromSeconds(stage_round_end_quick_time), stageRoundStart);
                else
                    await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), stageRoundStart);
            }
        }

        private async Task updateUserStats()
        {
            MatchmakingUser? firstPlaceUser = state.Users.FirstOrDefault(u => u.Placement == 1);

            // Can be null in the case none of the users played a map.
            if (firstPlaceUser == null)
                return;

            using (var db = dbFactory.GetInstance())
                await db.IncrementMatchmakingFirstPlacementsAsync(firstPlaceUser.UserId);
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

        private async Task changeStage(MatchmakingRoomStatus status)
        {
            state.RoomStatus = status;
            await hub.NotifyMatchRoomStateChanged(room);
        }

        private async Task startCountdown(TimeSpan duration, Func<ServerMultiplayerRoom, Task> continuation)
        {
            await room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = duration
            }, continuation);
        }

        private bool allUsersIdle()
        {
            return room.Users.All(u => u.State == MultiplayerUserState.Idle);
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
