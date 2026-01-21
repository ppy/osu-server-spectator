// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Standard
{
    public class TeamVersusMatchController : StandardMatchController
    {
        private readonly ServerMultiplayerRoom room;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly TeamVersusRoomState state;

        public TeamVersusMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventDispatcher eventDispatcher)
            : base(room, hub, dbFactory, eventDispatcher)
        {
            this.room = room;
            this.eventDispatcher = eventDispatcher;

            room.MatchState = state = TeamVersusRoomState.CreateDefault();
        }

        public override async Task Initialise()
        {
            await base.Initialise();

            await eventDispatcher.PostMatchRoomStateChangedAsync(room.RoomID, room.MatchState);
        }

        public override async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            await base.HandleUserJoined(user);

            user.MatchState = new TeamVersusUserState { TeamID = getBestAvailableTeam() };
            await eventDispatcher.PostMatchUserStateChangedAsync(room.RoomID, user.UserID, user.MatchState);
        }

        public override async Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            await base.HandleUserRequest(user, request);

            switch (request)
            {
                case ChangeTeamRequest changeTeam:
                    if (state.Teams.All(t => t.ID != changeTeam.TeamID))
                        throw new InvalidStateException("Attempted to set team out of valid range");

                    if (user.MatchState is TeamVersusUserState userState)
                        userState.TeamID = changeTeam.TeamID;

                    await eventDispatcher.PostMatchUserStateChangedAsync(room.RoomID, user.UserID, user.MatchState);
                    break;
            }
        }

        /// <summary>
        /// For a user joining the room, this will provide the most appropriate team for the new user to keep the room balanced.
        /// </summary>
        private int getBestAvailableTeam()
        {
            // initially check for any teams which don't yet have players, but are lower than TeamCount.
            foreach (var team in state.Teams)
            {
                if (room.Users.All(u => (u.MatchState as TeamVersusUserState)?.TeamID != team.ID))
                    return team.ID;
            }

            var countsByTeams = room.Users
                                    .GroupBy(u => (u.MatchState as TeamVersusUserState)?.TeamID)
                                    .Where(g => g.Key.HasValue)
                                    .OrderBy(g => g.Count());

            return countsByTeams.First().Key ?? 0;
        }

        public override MatchStartedEventDetail GetMatchDetails()
        {
            var teams = new Dictionary<int, room_team>();

            foreach (var user in room.Users)
            {
                if (user.MatchState is TeamVersusUserState userState)
                    teams[user.UserID] = userState.TeamID == 0 ? room_team.red : room_team.blue;
            }

            return new MatchStartedEventDetail
            {
                room_type = database_match_type.team_versus,
                teams = teams
            };
        }
    }
}
