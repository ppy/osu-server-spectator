// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Entities
{
    /// <summary>
    /// Maintains the connection state of a single client (notably, client, not user) across multiple hubs.
    /// </summary>
    public class ConnectionState
    {
        /// <summary>
        /// The unique ID of the JWT the user is using to authenticate.
        /// This is used to control user uniqueness.
        /// </summary>
        public readonly string TokenId;

        /// <summary>
        /// The connection IDs of the user for each hub type.
        /// </summary>
        /// <remarks>
        /// In SignalR, connection IDs are unique per connection.
        /// Because we use multiple hubs and a user is expected to be connected to each hub individually,
        /// we use a dictionary to track connections across all hubs for a specific user.
        /// </remarks>
        public readonly Dictionary<Type, string> ConnectionIds = new Dictionary<Type, string>();

        public ConnectionState(HubLifetimeContext context)
        {
            TokenId = context.Context.GetTokenId();

            RegisterConnectionId(context);
        }

        /// <summary>
        /// Registers the provided hub/connection context, replacing any existing connection for the hub type.
        /// </summary>
        /// <param name="context">The hub context to retrieve information from.</param>
        public void RegisterConnectionId(HubLifetimeContext context)
            => ConnectionIds[context.Hub.GetType()] = context.Context.ConnectionId;
    }
}
