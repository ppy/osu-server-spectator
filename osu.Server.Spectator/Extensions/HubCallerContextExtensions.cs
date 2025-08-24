// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
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
            var httpContext = context.GetHttpContext();
            if (httpContext == null)
                throw new InvalidOperationException("Unable to retrieve HttpContext from HubCallerContext.");

            if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader) || string.IsNullOrEmpty(authHeader))
                throw new InvalidOperationException("Authorization header is missing from the request.");

            const string bearer_prefix = "Bearer ";
            var headerValue = authHeader.ToString();
            if (!headerValue.StartsWith(bearer_prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Authorization header is not a Bearer token.");

            var token = headerValue.Substring(bearer_prefix.Length).Trim();

            // 解析 JWT 获取 sub 字段
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub");
            var subValue = subClaim?.Value as string;
            if (string.IsNullOrEmpty(subValue))
                throw new InvalidOperationException("JWT does not contain 'sub' claim.");

            if (!int.TryParse(subValue, out int userId))
                throw new InvalidOperationException($"Invalid user id in JWT 'sub' claim: {subValue}");

            return userId;
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
