// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Entities
{
    public class ConnectionState
    {
        /// <summary>
        /// The unique ID of the JWT the user is using to authenticate.
        /// This is used to control user uniqueness.
        /// </summary>
        public readonly string TokenId;

        /// <summary>
        /// The connection IDs of the user.
        /// </summary>
        /// <remarks>
        /// In SignalR, connection IDs are unique per user, <em>and</em> per hub instance.
        /// Therefore, to keep track of all of them, a dictionary is necessary.
        /// </remarks>
        public readonly Dictionary<Type, string> ConnectionIds = new Dictionary<Type, string>();

        public ConnectionState(HubLifetimeContext context)
        {
            TokenId = context.Context.GetTokenId();

            RegisterConnectionId(context);
        }

        public void RegisterConnectionId(HubLifetimeContext context)
            => ConnectionIds.Add(context.Hub.GetType(), context.Context.ConnectionId);
    }
}
