// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Entities
{
    public class ConnectionState : ClientState
    {
        public readonly string TokenId;

        public ConnectionState(in string connectionId, in int userId, in string tokenId)
            : base(in connectionId, in userId)
        {
            this.TokenId = tokenId;
        }

        public ConnectionState(HubCallerContext context)
            : this(context.ConnectionId, context.GetUserId(), context.GetTokenId())
        {
        }
    }
}
