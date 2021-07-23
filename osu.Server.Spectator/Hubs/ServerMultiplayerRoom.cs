// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class ServerMultiplayerRoom : MultiplayerRoom
    {
        private MatchRuleset matchRuleset;

        public MatchRuleset MatchRuleset
        {
            get => matchRuleset;
            set
            {
                matchRuleset = value;

                foreach (var u in Users)
                    matchRuleset.HandleUserJoined(u);
            }
        }

        public ServerMultiplayerRoom(long roomId)
            : base(roomId)
        {
            matchRuleset = new HeadToHeadRuleset(this);
        }
    }
}
