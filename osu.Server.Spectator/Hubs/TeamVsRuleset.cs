// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;

namespace osu.Server.Spectator.Hubs
{
    public class TeamVsRuleset : MatchRuleset
    {
        public TeamVsRuleset(MultiplayerRoom room)
            : base(room)
        {
        }

        public override void HandleUserJoined(MultiplayerRoomUser user)
        {
            base.HandleUserJoined(user);
            user.MatchRulesetState = new TeamVsMatchUserState { TeamID = bestAvailableTeam() };
        }

        public override void HandleUserRequest(MultiplayerRoomUser user, MatchRulesetUserRequest request)
        {
            switch (request)
            {
                case ChangeTeamRequest changeTeam:
                    if (user.MatchRulesetState is TeamVsMatchUserState state)
                        state.TeamID = changeTeam.TeamID;
                    break;
            }
        }

        /// <summary>
        /// For a user joining the room, this will provide the most appropriate team for the new user to keep the room balanced.
        /// </summary>
        private int bestAvailableTeam()
        {
            var countsByTeams = Room.Users
                                    .GroupBy(u => (u.MatchRulesetState as TeamVsMatchUserState)?.TeamID)
                                    .OrderBy(g => g.Count());

            return countsByTeams.First().Key ?? 0;
        }
    }
}
