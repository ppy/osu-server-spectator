// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Utils;
using osu.Game.Extensions;
using osu.Game.Online;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MatchmakingImplementation : MatchTypeImplementation
    {
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

        /// <summary>
        /// The number of points awarded for each placement position (index 0 = #1, index 7 = #8).
        /// </summary>
        private static readonly int[] placement_points = [8, 7, 6, 5, 4, 3, 2, 1];

        public override IMultiplayerQueue Queue { get; }

        private readonly IDatabaseFactory dbFactory;
        private readonly MatchmakingRoomState state;
        private readonly HashSet<UserPick> picks = new HashSet<UserPick>();

        public MatchmakingImplementation(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
            : base(room, hub)
        {
            this.dbFactory = dbFactory;

            Queue = new MultiplayerMatchmakingQueue(room, hub, dbFactory);

            room.MatchState = state = new MatchmakingRoomState();
        }

        public override async Task Initialise()
        {
            await base.Initialise();
            await Hub.NotifyMatchRoomStateChanged(Room);
        }

        public override async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            await base.HandleUserJoined(user);

            if (Room.Users.Count == MATCHMAKING_ROOM_SIZE)
                await stageRoundStart(Room);
        }

        public override async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            await base.HandleUserStateChanged(user);

            if (state.RoomStatus != MatchmakingRoomStatus.Results)
                return;

            if (allUsersIdle())
                await stageRoundStart(Room);
        }

        public override async Task HandleUserBeatmapAvailabilityChanged(MultiplayerRoomUser user, BeatmapAvailability availability)
        {
            await base.HandleUserBeatmapAvailabilityChanged(user, availability);

            switch (state.RoomStatus)
            {
                case MatchmakingRoomStatus.SelectBeatmap:
                case MatchmakingRoomStatus.PrepareBeatmap:
                    if (availability.State == DownloadState.LocallyAvailable)
                        await changeUserState(user, MultiplayerUserState.Ready);
                    else
                        await changeUserState(user, MultiplayerUserState.Idle);

                    if (allUsersReady())
                        await stagePrepareGameplay(Room);
                    break;
            }
        }

        public override async Task HandleMatchComplete()
        {
            await base.HandleMatchComplete();
            await stageResults();
        }

        public async Task ToggleSelectionAsync(MultiplayerRoomUser user, long playlistItemId)
        {
            MultiplayerPlaylistItem? item = Room.Playlist.SingleOrDefault(item => item.ID == playlistItemId);

            if (item == null)
                throw new InvalidStateException("Selected playlist item is not part of the room!");

            if (item.Expired)
                throw new InvalidStateException("Selected playlist item is expired!");

            picks.Add(new UserPick(user, playlistItemId));
            await Hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(Room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingSelectionToggled), user.UserID, playlistItemId);
        }

        private async Task stageRoundStart(ServerMultiplayerRoom _)
        {
            await returnUsersToRoom(Room);
            await startCountdown(MatchmakingRoomStatus.RoundStart, TimeSpan.FromSeconds(5), stagePicks);
        }

        private async Task stagePicks(ServerMultiplayerRoom _)
        {
            foreach (var selection in picks.Where(s => s.User != null))
                await Hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(Room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingSelectionToggled), selection.User, selection.ItemID);
            picks.Clear();

            await startCountdown(MatchmakingRoomStatus.UserPicks, TimeSpan.FromSeconds(10), stageSelectBeatmap);
        }

        private async Task stageSelectBeatmap(ServerMultiplayerRoom _)
        {
            if (picks.Count == 0)
                picks.AddRange(Room.Playlist.Select(item => new UserPick(null, item.ID)));

            state.CandidateItems = picks.Select(s => s.ItemID).ToArray();
            state.CandidateItem = state.CandidateItems[RNG.Next(0, state.CandidateItems.Length)];

            await startCountdown(MatchmakingRoomStatus.SelectBeatmap, TimeSpan.FromSeconds(10), stagePrepareBeatmap);
        }

        private async Task stagePrepareBeatmap(ServerMultiplayerRoom _)
        {
            await Hub.UnreadyAllUsers(Room, true);

            Room.Settings.PlaylistItemId = state.CandidateItem;
            await Hub.NotifySettingsChanged(Room, true);

            if (allUsersReady())
                await stagePrepareGameplay(Room);
            else
            {
                // If no users are ready, continue preparing beatmap. Otherwise, move onto gameplay with any ready users.
                await startCountdown(MatchmakingRoomStatus.PrepareBeatmap,
                    TimeSpan.FromMinutes(2),
                    _ => anyUsersReady() ? stagePrepareGameplay(Room) : stagePrepareBeatmap(Room));
            }
        }

        private async Task stagePrepareGameplay(ServerMultiplayerRoom _)
        {
            await startCountdown(MatchmakingRoomStatus.PrepareGameplay, TimeSpan.FromSeconds(5), stageGameplay);
        }

        private async Task stageGameplay(ServerMultiplayerRoom _)
        {
            await startCountdown(MatchmakingRoomStatus.Gameplay, TimeSpan.Zero, Hub.StartMatch);
        }

        private async Task stageResults()
        {
            SoloScore[] scores;
            using (var db = dbFactory.GetInstance())
                scores = (await db.GetAllScoresForPlaylistItem(Queue.CurrentItem.ID)).ToArray();

            // Index of each raw total score value.
            (int index, uint totalScore)[] totalScoreIndices = scores.OrderByDescending(s => s.total_score).Select((s, i) => (index: i, score: s.total_score)).ToArray();

            foreach (var score in scores)
            {
                // This makes sure that, for example, if the top two players have the same total score, they'll both receive #2 placement points.
                int placement = totalScoreIndices.Last(t => t.totalScore == score.total_score).index;
                state.UserScores.AddPoints((int)score.user_id, placement, placement_points[placement]);
            }

            state.UserScores.AdjustPlacements();

            await Hub.NotifyMatchRoomStateChanged(Room);
            await startCountdown(MatchmakingRoomStatus.Results, TimeSpan.FromSeconds(30), stageRoundStart);
        }

        private async Task returnUsersToRoom(ServerMultiplayerRoom _)
        {
            foreach (var user in Room.Users.Where(u => u.State != MultiplayerUserState.Idle))
                await changeUserState(user, MultiplayerUserState.Idle);
        }

        private async Task changeUserState(MultiplayerRoomUser user, MultiplayerUserState newState)
        {
            await Hub.ChangeAndBroadcastUserState(Room, user, newState);
            await Hub.UpdateRoomStateIfRequired(Room);
        }

        private async Task startCountdown(MatchmakingRoomStatus status, TimeSpan duration, Func<ServerMultiplayerRoom, Task> continuation)
        {
            if (status != state.RoomStatus)
            {
                state.RoomStatus = status;
                await Hub.NotifyMatchRoomStateChanged(Room);
            }

            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = duration
            }, continuation);
        }

        private bool allUsersIdle()
        {
            return Room.Users.All(u => u.State == MultiplayerUserState.Idle);
        }

        private bool allUsersReady()
        {
            return Room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        private bool anyUsersReady()
        {
            return Room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        public override MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };

        private readonly record struct UserPick(MultiplayerRoomUser? User, long ItemID);
    }
}
