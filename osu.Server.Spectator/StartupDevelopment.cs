// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace osu.Server.Spectator
{
    public class StartupDevelopment : Startup
    {
        protected override void ConfigureAuthentication(IServiceCollection services)
        {
            services.AddLocalAuthentication();
        }
    }

    [UsedImplicitly]
    public class LocalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private static int userIDCounter = 2;

        /// <summary>
        /// The name of the authorisation scheme that this handler will respond to.
        /// </summary>
        public const string AUTH_SCHEME = "LocalAuth";

        public LocalAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        /// <summary>
        /// Marks all authentication requests as successful, and injects required user claims.
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var nameIdentifierClaim = createNameIdentifierClaim();
            var clientIdClaim = createClientIdClaim();

            var authenticationTicket = new AuthenticationTicket(
                new ClaimsPrincipal(new[] { new ClaimsIdentity(new[] { nameIdentifierClaim, clientIdClaim }, AUTH_SCHEME) }),
                new AuthenticationProperties(), AUTH_SCHEME);

            return Task.FromResult(AuthenticateResult.Success(authenticationTicket));
        }

        private Claim createNameIdentifierClaim()
        {
            string? userIdString = null;

            if (Context.Request.Headers.TryGetValue("user_id", out var userIdValue))
                userIdString = userIdValue;

            userIdString ??= Interlocked.Increment(ref userIDCounter).ToString();

            var nameIdentifierClaim = new Claim(ClaimTypes.NameIdentifier, userIdString);
            return nameIdentifierClaim;
        }

        private Claim createClientIdClaim()
        {
            string? clientIdString = null;

            if (Context.Request.Headers.TryGetValue("client_id", out var clientIdValue))
                clientIdString = clientIdValue;

            clientIdString ??= Guid.NewGuid().ToString();

            var clientIdClaim = new Claim("jti", clientIdString);
            return clientIdClaim;
        }
    }

    public class LocalIdentity : IIdentity
    {
        public string AuthenticationType => LocalAuthenticationHandler.AUTH_SCHEME;
        public bool IsAuthenticated => true;
        public string Name { get; }

        public LocalIdentity(string name)
        {
            Name = name;
        }
    }

    public static class LocalAuthenticationHandlerExtensions
    {
        public static AuthenticationBuilder AddLocalAuthentication(this IServiceCollection services)
        {
            return services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = LocalAuthenticationHandler.AUTH_SCHEME;
                options.DefaultChallengeScheme = LocalAuthenticationHandler.AUTH_SCHEME;
            }).AddScheme<AuthenticationSchemeOptions, LocalAuthenticationHandler>(LocalAuthenticationHandler.AUTH_SCHEME, opt => { });
        }
    }
}
