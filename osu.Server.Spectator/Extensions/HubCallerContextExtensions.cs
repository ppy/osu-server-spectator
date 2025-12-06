// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Primitives;
using osu.Game.Online;

namespace osu.Server.Spectator.Extensions
{
    public static class HubCallerContextExtensions
    {
        /// <summary>
        /// Returns the osu! user id for the supplied <paramref name="context"/>.
        /// </summary>
        public static int GetUserId(this HubCallerContext context)
        {
            if (context.UserIdentifier == null)
                throw new InvalidOperationException($"Attempted to get user id with null {nameof(context.UserIdentifier)}");

            return int.Parse(context.UserIdentifier);
        }

        /// <summary>
        /// Returns the ID of the authorisation token (more accurately, the <c>jti</c> claim)
        /// for the supplied <paramref name="context"/>.
        /// This is used for the purpose of identifying individual client instances
        /// and preventing multiple concurrent sessions from being active.
        /// </summary>
        public static string GetTokenId(this HubCallerContext context)
        {
            return context.User?.FindFirst(claim => claim.Type == "jti")?.Value
                   ?? throw new InvalidOperationException("Could not retrieve JWT ID claim from token");
        }

        /// <summary>
        /// Returns the client version hash for the supplied <paramref name="context"/>, if it is available.
        /// </summary>
        /// <remarks>
        /// This method can only return a valid version hash when called in
        /// <see cref="Hub.OnConnectedAsync"/> or <see cref="IHubFilter.OnConnectedAsync"/>,
        /// as only the initial connection HTTP request will contain the client version hash.
        /// </remarks>
        public static string? GetVersionHash(this HubCallerContext context)
        {
            if (context.GetHttpContext()?.Request.Headers.TryGetValue(HubClientConnector.VERSION_HASH_HEADER, out StringValues headerValue) != true)
                return null;

            string versionHash = headerValue;

            // The token is 82 chars long, and the clientHash is the first 32 of those.
            // See: https://github.com/ppy/osu-web/blob/7be19a0fe0c9fa2f686e4bb686dbc8e9bf7bcf84/app/Libraries/ClientCheck.php#L92
            if (versionHash?.Length >= 82)
                versionHash = versionHash.Substring(versionHash.Length - 82, 32);

            return versionHash;
        }
    }
}
