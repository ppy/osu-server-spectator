// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
        private readonly ILoggerFactory loggerFactory;

        public ConfigureJwtBearerOptions(IDatabaseFactory databaseFactory, ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.loggerFactory = loggerFactory;
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
                    int tokenUserId = int.Parse(jwtToken.Subject);

                    using (var db = databaseFactory.GetInstance())
                    {
                        // check expiry/revocation against database
                        var userId = await db.GetUserIdFromTokenAsync(jwtToken);

                        if (userId != tokenUserId)
                        {
                            loggerFactory.CreateLogger("JsonWebToken").LogInformation("Token revoked or expired");
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

                    if (int.TryParse(jwtToken.Subject, out int tokenUserId))
                    {
                        // token has non-empty subject => issued via authorization code flow
                        // check expiry/revocation against database
                        var userId = await db.GetUserIdFromTokenAsync(jwtToken);

                        if (userId != tokenUserId)
                        {
                            loggerFactory.CreateLogger("JsonWebToken").LogInformation("Token revoked or expired");
                            context.Fail("Token has expired or been revoked");
                        }
                    }
                    else
                    {
                        // no subject => token issued via client_credentials flow with delegation
                        // check expiry/revocation against database
                        var resourceOwnerId = await db.GetDelegatedResourceOwnerIdFromTokenAsync(jwtToken);

                        if (resourceOwnerId == null)
                        {
                            loggerFactory.CreateLogger("JsonWebToken").LogInformation("Token revoked or expired");
                            context.Fail("Token has expired or been revoked");
                            return;
                        }

                        // the token is issued with no user associated with it.
                        // however we've checked above that the token has been issued with the `delegation` scope
                        // which means it is permissible to delegate actions from this client to its owner.
                        // therefore, append a relevant claim manually so that we can use it later to identify the user.
                        var identity = new ClaimsIdentity();
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, resourceOwnerId.Value.ToString()));
                        context.Principal!.AddIdentity(identity);
                    }
                },
            };
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
