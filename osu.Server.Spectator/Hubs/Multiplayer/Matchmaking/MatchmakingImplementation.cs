// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
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

        private readonly MatchmakingRoomState state;

        public MatchmakingImplementation(ServerMultiplayerRoom room, IMultiplayerHubContext hub)
            : base(room, hub)
        {
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
                state.RoomStatus = MatchmakingRoomStatus.WaitForNextRound;
                await Hub.NotifyMatchRoomStateChanged(Room);
                await Room.StartCountdown(new MatchmakingStatusCountdown
                {
                    Status = state.RoomStatus,
                    TimeRemaining = TimeSpan.FromSeconds(5)
                }, beginNextRound);
            }
        }

        private async Task beginNextRound(ServerMultiplayerRoom _)
        {
            state.RoomStatus = MatchmakingRoomStatus.Pick;
            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(30)
            }, selectBeatmap);
        }

        private async Task selectBeatmap(ServerMultiplayerRoom _)
        {
            state.RoomStatus = MatchmakingRoomStatus.WaitForSelection;
            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(15)
            }, beginPlay);
        }

        private async Task beginPlay(ServerMultiplayerRoom _)
        {
            state.RoomStatus = MatchmakingRoomStatus.WaitForStart;

            // Todo: Select a beatmap here...

            await Hub.NotifyMatchRoomStateChanged(Room);
            await Room.StartCountdown(new MatchmakingStatusCountdown
            {
                Status = state.RoomStatus,
                TimeRemaining = TimeSpan.FromSeconds(5)
            }, r => Hub.StartMatch(r, false));
        }

        public override MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };
    }
}
