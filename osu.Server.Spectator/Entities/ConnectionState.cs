// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online;
using osu.Server.Spectator.Extensions;

#pragma warning disable CS0618 // Type or member is obsolete

namespace osu.Server.Spectator.Entities
{
    /// <summary>
    /// Maintains the connection state of a single client (notably, client, not user) across multiple hubs.
    /// </summary>
    public class ConnectionState
    {
        /// <summary>
        /// A client-side generated GUID identifying the client instance connecting to this server.
        /// This is used to control user uniqueness.
        /// </summary>
        public readonly Guid? ClientSessionId;

        /// <summary>
        /// The unique ID of the JWT the user is using to authenticate.
        /// </summary>
        /// <remarks>
        /// This was previously used as a method of controlling user uniqueness / limiting concurrency,
        /// but it turned out to be a bad fit for the purpose (see https://github.com/ppy/osu/issues/26338#issuecomment-2222935517).
        /// </remarks>
        [Obsolete("Use ClientSessionId instead.")]
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

            if (tryGetClientSessionID(context, out var clientSessionId))
                ClientSessionId = clientSessionId;

            RegisterConnectionId(context);
        }

        /// <summary>
        /// Registers the provided hub/connection context, replacing any existing connection for the hub type.
        /// </summary>
        /// <param name="context">The hub context to retrieve information from.</param>
        public void RegisterConnectionId(HubLifetimeContext context)
            => ConnectionIds[context.Hub.GetType()] = context.Context.ConnectionId;

        public bool IsConnectionFromSameClient(HubLifetimeContext context)
        {
            if (tryGetClientSessionID(context, out var clientSessionId))
                return ClientSessionId == clientSessionId;

            return TokenId == context.Context.GetTokenId();
        }

        public bool IsInvocationPermitted(HubInvocationContext context)
        {
            bool hubRegistered = ConnectionIds.TryGetValue(context.Hub.GetType(), out string? registeredConnectionId);
            bool connectionIdMatches = registeredConnectionId == context.Context.ConnectionId;

            return hubRegistered && connectionIdMatches;
        }

        public bool CanCleanUpConnection(HubLifetimeContext context)
        {
            bool hubRegistered = ConnectionIds.TryGetValue(context.Hub.GetType(), out string? registeredConnectionId);
            bool connectionIdMatches = registeredConnectionId == context.Context.ConnectionId;

            return hubRegistered && connectionIdMatches;
        }

        private static bool tryGetClientSessionID(HubLifetimeContext context, out Guid clientSessionId)
        {
            clientSessionId = Guid.Empty;
            return context.Context.GetHttpContext()?.Request.Headers.TryGetValue(HubClientConnector.CLIENT_SESSION_ID_HEADER, out var value) == true
                   && Guid.TryParse(value, out clientSessionId);
        }
    }
}
