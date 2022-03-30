// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;

namespace osu.Server.Spectator.Hubs
{
    public class TeamVersus : MatchTypeImplementation
    {
        private readonly TeamVersusRoomState state;

        public TeamVersus(ServerMultiplayerRoom room, MultiplayerHubContext hub)
            : base(room, hub)
        {
            room.MatchState = state = TeamVersusRoomState.CreateDefault();

            Hub.UpdateMatchRoomState(room);
        }

        public override void HandleUserJoined(MultiplayerRoomUser user)
        {
            base.HandleUserJoined(user);

            user.MatchState = new TeamVersusUserState { TeamID = getBestAvailableTeam() };
            Hub.UpdateMatchUserState(Room, user);
        }

        public override void HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            switch (request)
            {
                case ChangeTeamRequest changeTeam:
                    if (state.Teams.All(t => t.ID != changeTeam.TeamID))
                        throw new InvalidStateException("Attempted to set team out of valid range");

                    if (user.MatchState is TeamVersusUserState userState)
                        userState.TeamID = changeTeam.TeamID;

                    Hub.UpdateMatchUserState(Room, user);
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
                if (Room.Users.All(u => (u.MatchState as TeamVersusUserState)?.TeamID != team.ID))
                    return team.ID;
            }

            var countsByTeams = Room.Users
                                    .GroupBy(u => (u.MatchState as TeamVersusUserState)?.TeamID)
                                    .Where(g => g.Key.HasValue)
                                    .OrderBy(g => g.Count());

            return countsByTeams.First().Key ?? 0;
        }
    }
}
