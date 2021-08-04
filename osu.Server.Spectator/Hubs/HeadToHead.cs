// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class HeadToHead : MatchTypeImplementation
    {
        public HeadToHead(ServerMultiplayerRoom room, IMultiplayerServerMatchCallbacks hub)
            : base(room, hub)
        {
        }

        public override void HandleUserJoined(MultiplayerRoomUser user)
        {
            base.HandleUserJoined(user);

            if (user.MatchState != null)
            {
                // we don't need a state, but keep things simple by completely nulling the state.
                // this allows the client to see a user state change and handle match type specifics based on that alone.
                user.MatchState = null;
                Hub.UpdateMatchUserState(Room, user);
            }
        }
    }
}
