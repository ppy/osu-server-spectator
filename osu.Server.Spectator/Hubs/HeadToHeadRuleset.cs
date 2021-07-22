// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class HeadToHeadRuleset : MatchRuleset
    {
        public HeadToHeadRuleset(ServerMultiplayerRoom room)
            : base(room)
        {
        }

        public override void HandleUserRequest(MatchRulesetUserRequest request)
        {
        }
    }
}
