// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Microsoft.AspNetCore.SignalR;

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
    }
}
