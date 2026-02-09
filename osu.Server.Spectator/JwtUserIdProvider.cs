// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Hubs.Referee;

namespace osu.Server.Spectator
{
    /// <remarks>
    /// This class is used by SignalR to populate <see cref="HubCallerContext.UserIdentifier"/> from a <see cref="ClaimsPrincipal"/>.
    /// The <see cref="DefaultUserIdProvider">default implementation</see> uses <see cref="ClaimTypes.NameIdentifier"/> too,
    /// and it worked fine until wanting to add support for <c>client_credentials</c> tokens in <see cref="RefereeHub"/>.
    /// Those tokens have the <see cref="ClaimTypes.NameIdentifier"/> claim with an empty value, even if they are allowed delegation,
    /// so <see cref="ConfigureJwtBearerOptions"/> adds a second non-empty copy of this claim.
    /// <see cref="DefaultUserIdProvider"/> would pick the first matching claim which would be the one with the empty value;
    /// this implementation picks the non-empty one.
    /// </remarks>
    public class JwtUserIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection)
        {
            var claim = connection.User.FindFirst(claim => claim.Type == ClaimTypes.NameIdentifier && !string.IsNullOrWhiteSpace(claim.Value));
            return claim?.Value;
        }
    }
}
