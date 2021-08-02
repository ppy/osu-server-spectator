// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR()
                    .AddMessagePackProtocol()
                    .AddNewtonsoftJsonProtocol(options =>
                    {
                        options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                        options.PayloadSerializerSettings = new JsonSerializerSettings
                        {
                            // TODO: This is required to make root class serialisation work in the case of derived classes.
                            // Optimally, we only want to set `TypeNameHandling.Auto` here, as this will currently be sending overly verbose responses.
                            //
                            // This is a shortcoming of SignalR, in that it does not pass the (base) class specification through to NewtonsoftJson's Serialize method.
                            // See JsonSerializationTests's calls to SerializeObject for an example of how it should be done.
                            //
                            // We are relying on this for *all* connections to MultiplayerHubs currently, as the same issue exists in MessagePack but is harder to work around.
                            // This is required for MatchRuleset types, which are regularly sent as derived implementations where that type is not conveyed in the invocation signature itself.
                            //
                            // Some references:
                            // https://github.com/neuecc/MessagePack-CSharp/issues/1171 ("it's not messagepack's issue")
                            // https://github.com/dotnet/aspnetcore/issues/30096 ("it's definitely broken")
                            // https://github.com/dotnet/aspnetcore/issues/7298 (current tracking issue, though weirdly described as a javascript client issue)
                            TypeNameHandling = TypeNameHandling.All
                        };
                    });

            services.AddHubEntities()
                    .AddDatabaseServices();

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
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
            services.AddAuthentication(config =>
                    {
                        config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                        config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddJwtBearer(null); // options will be injected through DI, via the singleton registration above.
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
            app.UseEndpoints(endpoints => { endpoints.MapHub<MultiplayerHub>("/multiplayer"); });
        }
    }
}
