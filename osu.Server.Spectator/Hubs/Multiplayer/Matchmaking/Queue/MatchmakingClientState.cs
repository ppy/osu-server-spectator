// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Matchmaking;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingClientState
    {
        public readonly string ConnectionId;
        public readonly int UserId;
        public MatchmakingSettings Settings = new MatchmakingSettings();

        public MatchmakingClientState(HubCallerContext context)
        {
            ConnectionId = context.ConnectionId;
            UserId = context.GetUserId();
        }

        public MatchmakingClientState(MultiplayerClientState state)
        {
            ConnectionId = state.ConnectionId;
            UserId = state.UserId;
        }
    }
}
