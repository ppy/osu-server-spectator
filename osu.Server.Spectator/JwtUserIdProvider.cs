// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Metadata;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Referee;
using osu.Server.Spectator.Hubs.Spectator;

namespace osu.Server.Spectator
{
    /// <summary>
    /// This class is used by SignalR to populate <see cref="HubCallerContext.UserIdentifier"/> from a <see cref="ClaimsPrincipal"/>.
    /// <seealso cref="HubCallerContext.UserIdentifier"/> is important as it is later used by <see cref="HubCallerContextExtensions.GetUserId"/>
    /// to get a user ID from a <see cref="HubCallerContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spectator server deals with tokens issued by osu-web via the following grant types:
    /// <list type="bullet">
    /// <item>
    /// <c>password</c> grant: Tokens issued to lazer game clients, in order to access
    /// <see cref="MetadataHub"/>, <see cref="MultiplayerHub"/>, and <see cref="SpectatorHub"/>.
    /// For this token type, the token will have <see cref="ClaimTypes.NameIdentifier"/> and <c>sub</c> claims
    /// containing the osu! user ID of the user playing.
    /// </item>
    /// <item>
    /// <c>authorization_code</c> grant: Tokens issued to lazer refereeing clients, in order to access <see cref="RefereeHub"/>.
    /// For this token type, the token will have <see cref="ClaimTypes.NameIdentifier"/> and <c>sub</c> claims
    /// containing the osu! user ID of the user who authorised with the refereeing client.
    /// </item>
    /// <item>
    /// <c>client_credentials</c> grant: Tokens issued to lazer refereeing clients, in order to access <see cref="RefereeHub"/>.
    /// Tokens with this grant should also contain <c>delegation</c> scope to work with <see cref="RefereeHub"/>,
    /// which means actions will be performed on behalf of the OAuth client owner.
    /// For this token type, the token will have <see cref="ClaimTypes.NameIdentifier"/> and <c>sub</c> claims
    /// containing the <b>internal ID of the OAuth client used</b> from the <c>oauth_clients</c> database table.
    /// </item>
    /// </list>
    /// The ID space conflict arising from the fact that <seealso cref="ClaimTypes.NameIdentifier"/> and <c>sub</c> claims
    /// sometimes point at osu! users and sometimes at OAuth clients,
    /// makes both claims unreliable for user identification purposes.
    /// This only started being the case after an osu-web-side Passport (authentication library) update to version 13 (https://github.com/ppy/osu-web/pull/13237).
    /// Therefore, to insulate against this sort of thing happening again in the future, <see cref="ConfigureJwtBearerOptions"/> is set up
    /// to inject the <see cref="USER_ID_CLAIM_TYPE"/> claim into the relevant <see cref="ClaimsPrincipal"/> so that it can be read back safely here.
    /// </para>
    /// </remarks>
    public class JwtUserIdProvider : IUserIdProvider
    {
        public const string USER_ID_CLAIM_TYPE = "osu_user_id";

        public string? GetUserId(HubConnectionContext connection)
        {
            var claim = connection.User.FindFirst(claim => claim.Type == USER_ID_CLAIM_TYPE);
            return claim?.Value;
        }
    }
}
