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
