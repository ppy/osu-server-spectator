// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Utils;
using osu.Game.Extensions;
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
        public const int MATCHMAKING_ROOM_SIZE = 1;

        public static readonly int[] BEATMAP_IDS =
        [
            186036, 221862, 222774, 222775, 384926, 412968, 415098, 415099, 419747, 424260, 436442, 437721, 438096, 440820, 441920, 454385, 492862, 497679, 497680, 526342, 526935, 550385, 554956,
            554957, 558078, 609843, 613823, 614095, 618865, 626245, 626495, 628637, 628995, 630747, 632127, 634101, 635625, 641039, 642687, 642689, 644573, 644837, 645765, 645783, 647026, 649186,
            649545, 650188, 651348, 654922, 657476, 661216, 665240, 666332, 675157, 695694, 700000, 705760, 710881, 713594, 714221, 718670, 720172, 723308, 727447, 730770, 733943, 735990, 737284,
            737818, 738062, 739825, 739826, 743069, 743192, 743395, 743469, 746533, 746545, 746840, 749857, 750961, 751598, 751599, 755258, 755932, 756249, 757970, 758383, 759638, 759911, 761722,
            762086, 762511, 762762, 763308, 764193, 764271, 765873, 766080, 767203, 767252, 767253, 767988, 769395, 770727, 772067, 772987, 773155, 773410, 775561, 778792, 780119, 780140, 780863,
            780864, 780865, 781495, 781872, 781873, 783305, 784601, 785409, 787961, 788245, 788676, 789771, 789820
        ];

        public override IMultiplayerQueue Queue { get; }

        private readonly MatchmakingRoomState state;
        private readonly HashSet<UserBeatmapSelection> selections = new HashSet<UserBeatmapSelection>();

        public MatchmakingImplementation(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
            : base(room, hub)
        {
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
            {
                state.RoomStatus = MatchmakingRoomStatus.RoundStart;
                await Hub.NotifyMatchRoomStateChanged(Room);
                await Room.StartCountdown(new MatchmakingStatusCountdown
                {
                    Status = state.RoomStatus,
                    TimeRemaining = TimeSpan.FromSeconds(5)
                }, beginNextRound);
            }
        }

        public override async Task HandleUserStateChanged()
        {
            await base.HandleUserStateChanged();

            if (state.RoomStatus != MatchmakingRoomStatus.Gameplay || Room.Users.Any(u => u.State != MultiplayerUserState.Idle))
                return;

            state.RoomStatus = MatchmakingRoomStatus.RoundStart;
            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(5)
            }, beginNextRound);
        }

        public override async Task HandleMatchComplete()
        {
            await base.HandleMatchComplete();

            // Todo: Award points.

            if (Room.Users.Any(u => u.State != MultiplayerUserState.Idle))
            {
                await Hub.NotifyMatchRoomStateChanged(Room);
                await Room.StartCountdown(new MatchmakingStatusCountdown
                {
                    Status = state.RoomStatus,
                    TimeRemaining = TimeSpan.FromSeconds(30)
                }, returnUsersToRoom);
            }
        }

        public async Task ToggleSelectionAsync(MultiplayerRoomUser user, long playlistItemId)
        {
            MultiplayerPlaylistItem? item = Room.Playlist.SingleOrDefault(item => item.ID == playlistItemId);

            if (item == null)
                throw new InvalidStateException("Selected playlist item is not part of the room!");

            if (item.Expired)
                throw new InvalidStateException("Selected playlist item is expired!");

            selections.Add(new UserBeatmapSelection(user, playlistItemId));
            await Hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(Room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingSelectionToggled), user.UserID, playlistItemId);
        }

        private async Task beginNextRound(ServerMultiplayerRoom _)
        {
            // Ensure users are in an idle state.
            await returnUsersToRoom(Room);

            // Clear selections.
            foreach (var selection in selections.Where(s => s.User != null))
                await Hub.Context.Clients.Groups(MultiplayerHub.GetGroupId(Room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchmakingSelectionToggled), selection.User, selection.ItemID);
            selections.Clear();

            // Start the next round.
            state.RoomStatus = MatchmakingRoomStatus.PickBeatmap;
            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(10)
            }, selectBeatmap);
        }

        private async Task selectBeatmap(ServerMultiplayerRoom _)
        {
            if (selections.Count == 0)
                selections.AddRange(Room.Playlist.Select(item => new UserBeatmapSelection(null, item.ID)));

            state.RoomStatus = MatchmakingRoomStatus.Selection;
            state.CandidateItems = selections.Select(s => s.ItemID).ToArray();

            // Notify users of the new beatmap.
            Room.Settings.PlaylistItemId = state.CandidateItems[RNG.Next(0, state.CandidateItems.Length)];
            await Hub.NotifySettingsChanged(Room, true);

            // Notify users of the candidates.
            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(10)
            }, beginPlay);
        }

        private async Task beginPlay(ServerMultiplayerRoom _)
        {
            state.RoomStatus = MatchmakingRoomStatus.PrepareGameplay;

            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(5)
            }, async r =>
            {
                state.RoomStatus = MatchmakingRoomStatus.Gameplay;
                await Hub.NotifyMatchRoomStateChanged(Room);
                await Hub.StartMatch(r, false);
            });
        }

        private async Task returnUsersToRoom(ServerMultiplayerRoom _)
        {
            foreach (var user in Room.Users.Where(u => u.State != MultiplayerUserState.Idle))
                await Hub.ChangeAndBroadcastUserState(Room, user, MultiplayerUserState.Idle);
            await Hub.UpdateRoomStateIfRequired(Room);
        }

        public override MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };

        private readonly record struct UserBeatmapSelection(MultiplayerRoomUser? User, long ItemID);
    }
}
