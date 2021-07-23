// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;

namespace osu.Server.Spectator.Hubs
{
    public class TeamVsRuleset : MatchRuleset
    {
        public int TeamCount => 2; // eventually this will be extendable.

        public TeamVsRuleset(ServerMultiplayerRoom room)
            : base(room)
        {
        }

        public override void HandleUserJoined(MultiplayerRoomUser user)
        {
            base.HandleUserJoined(user);
            user.MatchRulesetState = new TeamVsMatchUserState { TeamID = getBestAvailableTeam() };
        }

        public override void HandleUserRequest(MultiplayerRoomUser user, MatchRulesetUserRequest request)
        {
            switch (request)
            {
                case ChangeTeamRequest changeTeam:
                    if (changeTeam.TeamID < 0 || changeTeam.TeamID >= TeamCount)
                        throw new InvalidStateException("Attempted to set team out of valid range");

                    if (user.MatchRulesetState is TeamVsMatchUserState state)
                        state.TeamID = changeTeam.TeamID;

                    Room.UpdateMatchRulesetUserState(Room, user);
                    break;
            }
        }

        /// <summary>
        /// For a user joining the room, this will provide the most appropriate team for the new user to keep the room balanced.
        /// </summary>
        private int getBestAvailableTeam()
        {
            // initially check for any teams which don't yet have players, but are lower than TeamCount.
            for (int i = 0; i < TeamCount; i++)
            {
                if (Room.Users.Count(u => (u.MatchRulesetState as TeamVsMatchUserState)?.TeamID == i) == 0)
                    return i;
            }

            var countsByTeams = Room.Users
                                    .GroupBy(u => (u.MatchRulesetState as TeamVsMatchUserState)?.TeamID)
                                    .Where(g => g.Key.HasValue)
                                    .OrderBy(g => g.Count());

            return countsByTeams.First().Key ?? 0;
        }
    }
}
