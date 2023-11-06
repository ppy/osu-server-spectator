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
    }
}
