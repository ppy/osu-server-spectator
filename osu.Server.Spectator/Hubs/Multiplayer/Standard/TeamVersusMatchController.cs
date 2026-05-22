// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

        protected new TeamVersusRoomState State => (TeamVersusRoomState)base.State;

        public TeamVersusMatchController(ServerMultiplayerRoom room, IDatabaseFactory dbFactory, MultiplayerEventDispatcher eventDispatcher)
            : base(room, dbFactory, eventDispatcher)
        {
            this.room = room;
            this.eventDispatcher = eventDispatcher;
        }

        protected override StandardMatchRoomState CreateRoomState() => TeamVersusRoomState.CreateDefault();

        public override async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            if (user.Role != MultiplayerRoomUserRole.Referee)
            {
                user.MatchState = new TeamVersusUserState { TeamID = getBestAvailableTeam() };
                await eventDispatcher.PostMatchUserStateChangedAsync(room.RoomID, user.UserID, user.MatchState);
            }

            // ordering is important here - we want to have the user in a team already
            // so that `GetNextBestSlot()` can work as expected
            await base.HandleUserJoined(user);
        }

        public override async Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            await base.HandleUserRequest(user, request);

            switch (request)
            {
                case ChangeTeamRequest changeTeam:
                    if (State.Locked)
                        throw new InvalidStateException("Teams are currently locked.");

                    await ChangeUserTeam(user, changeTeam.TeamID);
                    break;
            }
        }

        /// <summary>
        /// Changes the <paramref name="user"/>'s team to <paramref name="newTeamId"/>.
        /// <see cref="TeamVersusRoomState.Locked"/> is purposefully NOT checked. Callers should decide whether they want to check that flag or not.
        /// </summary>
        public async Task ChangeUserTeam(MultiplayerRoomUser user, int newTeamId)
        {
            if (State.Teams.All(t => t.ID != newTeamId))
                throw new InvalidStateException("Attempted to set team out of valid range");

            if (user.Role == MultiplayerRoomUserRole.Referee)
                throw new InvalidStateException("Referees cannot join teams.");

            if (user.MatchState is TeamVersusUserState userState)
                userState.TeamID = newTeamId;

            await eventDispatcher.PostMatchUserStateChangedAsync(room.RoomID, user.UserID, user.MatchState);
        }

        /// <summary>
        /// For a user joining the room, this will provide the most appropriate team for the new user to keep the room balanced.
        /// </summary>
        private int getBestAvailableTeam()
        {
            // initially check for any teams which don't yet have players, but are lower than TeamCount.
            foreach (var team in State.Teams)
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

        protected override int GetNextBestSlot(MultiplayerRoomUser user, int?[] slots)
        {
            if (user.MatchState is not TeamVersusUserState userState)
                return base.GetNextBestSlot(user, slots);

            int teamSize = slots.Length / State.Teams.Count;
            int teamStartIndex = userState.TeamID * teamSize;

            int nextEmptySlotInTeam = Array.FindIndex(slots, teamStartIndex, teamSize, item => item == null);
            if (nextEmptySlotInTeam > 0)
                return nextEmptySlotInTeam;

            return base.GetNextBestSlot(user, slots);
        }

        public override MatchStartedEventDetail GetMatchDetails()
        {
            var details = base.GetMatchDetails();

            var teams = new Dictionary<int, room_team>();

            foreach (var user in room.Users)
            {
                if (user.MatchState is TeamVersusUserState userState && Enum.IsDefined(typeof(room_team), userState.TeamID))
                    teams[user.UserID] = (room_team)userState.TeamID;
            }

            details.teams = teams;
            return details;
        }
    }
}
