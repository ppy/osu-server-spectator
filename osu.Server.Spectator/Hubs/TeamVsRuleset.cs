// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    }
}
