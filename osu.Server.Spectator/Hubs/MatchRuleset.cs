// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public abstract class MatchRuleset
    {
        protected readonly MultiplayerRoom Room;

        protected MatchRuleset(MultiplayerRoom room)
        {
            this.Room = room;
        }

        /// <summary>
        /// Called when a user has requested a match ruleset specific action.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="request">The nature of the action.</param>
        public virtual void HandleUserRequest(MultiplayerRoomUser user, MatchRulesetUserRequest request)
        {
        }
    }
}
