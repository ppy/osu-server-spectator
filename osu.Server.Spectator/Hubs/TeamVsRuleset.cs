// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;

namespace osu.Server.Spectator.Hubs
{
    public class TeamVsRuleset : MatchRuleset
    {
        public override void HandleUserRequest(MatchRulesetUserRequest request)
        {
            switch (request)
            {
                case ChangeTeamRequest _:
                    // handle changeTeam.TeamID;
                    break;
            }
        }
    }
}
