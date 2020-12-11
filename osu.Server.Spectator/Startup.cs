// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR()
                    .AddNewtonsoftJsonProtocol(options => { options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore; });

            services.AddDistributedMemoryCache(); // replace with redis

            services.AddLogging(logging =>
            {
                // logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
                // logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);

                logging.ClearProviders();
                logging.AddConsole();

                // IdentityModelEventSource.ShowPII = true;
            });

            ConfigureAuthentication(services);

            services.AddAuthorization();
        }

        protected virtual void ConfigureAuthentication(IServiceCollection services)
        {
            var rsa = getKeyProvider();

            services.AddAuthentication(config =>
            {
                config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new RsaSecurityKey(rsa),
                    // TODO: check what "5" means.
                    ValidAudience = "5",
                    // TODO: figure out why this isn't included in the token.
                    ValidateIssuer = false,
                    ValidIssuer = "https://osu.ppy.sh/"
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var jwtToken = (JwtSecurityToken)context.SecurityToken;
                        int tokenUserId = int.Parse(jwtToken.Subject);

                        using (var conn = Database.GetConnection())
                        {
                            // check expiry/revocation against database
                            var userId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT user_id FROM oauth_access_tokens WHERE revoked = false AND expires_at > now() AND id = @id",
                                new { id = jwtToken.Id });

                            if (userId != tokenUserId)
                            {
                                Console.WriteLine("Token revoked or expired");
                                context.Fail("Token has expired or been revoked");
                            }
                        }
                    },
                    OnAuthenticationFailed = context =>
                    {
                        Console.WriteLine("Token authentication failed");
                        return Task.CompletedTask;
                    },
                };
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseWebSockets();

            app.UseEndpoints(endpoints => { endpoints.MapHub<SpectatorHub>("/spectator"); });
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
