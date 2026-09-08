// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Authentication
{
    public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        public const string LAZER_CLIENT_SCHEME = "lazer";
        public const string REFEREE_CLIENT_SCHEME = "referee";

        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger logger;

        public ConfigureJwtBearerOptions(IDatabaseFactory databaseFactory, ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.logger = loggerFactory.CreateLogger("JsonWebToken");
        }

        // this looks very scary, but ASP.NET never calls this and calls the named variant instead. don't ask why.
        public void Configure(JwtBearerOptions options)
            => throw new NotSupportedException();

        public void Configure(string? name, JwtBearerOptions options)
        {
            switch (name)
            {
                case LAZER_CLIENT_SCHEME:
                    configureLazerClientScheme(options);
                    return;

                case REFEREE_CLIENT_SCHEME:
                    configureRefereeClientScheme(options);
                    return;
            }
        }

        private void configureLazerClientScheme(JwtBearerOptions options)
        {
            var rsa = getKeyProvider();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = new RsaSecurityKey(rsa),
                ValidAudience = "5", // should match the client ID assigned to osu! in the osu-web target deploy.
                // TODO: figure out why this isn't included in the token.
                ValidateIssuer = false,
                ValidIssuer = "https://osu.ppy.sh/"
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var jwtToken = (JsonWebToken)context.SecurityToken;

                    using (var db = databaseFactory.GetInstance())
                    {
                        // check expiry/revocation against database
                        int? userId = await db.GetUserIdFromTokenAsync(jwtToken);

                        if (userId != null)
                            addUserIdToPrincipal(context.Principal!, userId.Value);
                        else
                        {
                            logger.LogInformation("Token revoked or expired");
                            context.Fail("Token has expired or been revoked");
                        }
                    }
                },
            };
        }

        private void configureRefereeClientScheme(JwtBearerOptions options)
        {
            var rsa = getKeyProvider();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = new RsaSecurityKey(rsa),
                // there could be multiple valid audiences here, so we're not checking.
                // the access to the referee API is controlled by possessing the `multiplayer.write_manage` scope instead,
                // and that scope is checked for at endpoint access time via `[Authorize]` attributes rather than at JWT validation time.
                ValidateAudience = false,
                // TODO: figure out why this isn't included in the token.
                ValidateIssuer = false,
                ValidIssuer = "https://osu.ppy.sh/"
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // see https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0#built-in-jwt-authentication
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var jwtToken = (JsonWebToken)context.SecurityToken;

                    using var db = databaseFactory.GetInstance();

                    // try getting the user ID from the token first.
                    // compare: https://github.com/ppy/osu-web/blob/877ce7dd09467024447781f6b745064e3094cde0/app/Models/OAuth/Token.php#L110-L122
                    int? userId = await db.GetUserIdFromTokenAsync(jwtToken);

                    if (userId != null)
                    {
                        // if the user ID is present, then the token is presumed to have been obtained via the `authorization_code` grant.
                        addUserIdToPrincipal(context.Principal!, userId.Value);
                        return;
                    }

                    // if the user ID was not retrieved from the token,
                    // it could still be a valid token obtained via the `client_credentials` grant with `delegation` scope.
                    int? resourceOwnerId = await db.GetDelegatedResourceOwnerIdFromTokenAsync(jwtToken);

                    if (resourceOwnerId != null)
                    {
                        addUserIdToPrincipal(context.Principal!, resourceOwnerId.Value);
                        return;
                    }

                    logger.LogInformation("Token revoked or expired");
                    context.Fail("Token has expired or been revoked");
                },
            };
        }

        /// <seealso cref="JwtUserIdProvider"/>
        private void addUserIdToPrincipal(ClaimsPrincipal claimsPrincipal, int userId)
        {
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(JwtUserIdProvider.USER_ID_CLAIM_TYPE, userId.ToString(CultureInfo.InvariantCulture)));
            claimsPrincipal.AddIdentity(identity);
        }

        /// <summary>
        /// borrowed from https://stackoverflow.com/a/54323524
        /// </summary>
        private static RSACryptoServiceProvider getKeyProvider()
        {
            string key = File.ReadAllText("oauth-public.key");

            key = key.Replace("-----BEGIN PUBLIC KEY-----", "");
            key = key.Replace("-----END PUBLIC KEY-----", "");
            key = key.Replace("\n", "");

            var keyBytes = Convert.FromBase64String(key);

            var asymmetricKeyParameter = PublicKeyFactory.CreateKey(keyBytes);
            var rsaKeyParameters = (RsaKeyParameters)asymmetricKeyParameter;
            var rsaParameters = new RSAParameters
            {
                Modulus = rsaKeyParameters.Modulus.ToByteArrayUnsigned(),
                Exponent = rsaKeyParameters.Exponent.ToByteArrayUnsigned()
            };

            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(rsaParameters);

            return rsa;
        }
    }
}
