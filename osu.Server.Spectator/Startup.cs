// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using osu.Game.Online;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Hubs.Metadata;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Referee;
using osu.Server.Spectator.Hubs.Spectator;

namespace osu.Server.Spectator
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            Action<HubOptions> configureClientHubOptions = options =>
            {
                // JSON hub protocol is enabled by default, but we use MessagePack.
                // Some models are not compatible with the JSON protocol, so we should never negotiate it.
                options.SupportedProtocols?.Remove("json");

                options.AddFilter<LoggingHubFilter>();
                options.AddFilter<ConcurrentConnectionLimiter>();
                options.AddFilter<ClientVersionChecker>();
            };

            services.AddSignalR()
                    .AddMessagePackProtocol(options =>
                    {
                        // This is required for match type states/events, which are regularly sent as derived implementations where that type is not conveyed in the invocation signature itself.
                        //
                        // Some references:
                        // https://github.com/neuecc/MessagePack-CSharp/issues/1171 ("it's not messagepack's issue")
                        // https://github.com/dotnet/aspnetcore/issues/30096 ("it's definitely broken")
                        // https://github.com/dotnet/aspnetcore/issues/7298 (current tracking issue, though weirdly described as a javascript client issue)
                        options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS;
                    })
                    .AddHubOptions<MetadataHub>(configureClientHubOptions)
                    .AddHubOptions<MultiplayerHub>(configureClientHubOptions)
                    .AddHubOptions<SpectatorHub>(configureClientHubOptions)
                    .AddHubOptions<RefereeHub>(options =>
                    {
                        options.SupportedProtocols?.Remove("messagepack");
                    });

            services.AddHubEntities()
                    .AddDatabaseServices()
                    .AddMemoryCache();

            services.AddDistributedMemoryCache(); // replace with redis

            services.AddLogging(logging =>
            {
                // logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
                // logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);

                logging.ClearProviders();
                logging.AddConsole();
#if !DEBUG
                logging.AddSentry(options => options.MinimumEventLevel = LogLevel.Warning);
#endif

                // IdentityModelEventSource.ShowPII = true;
            });

            // Allow a bit of extra time in addition to the graceful shutdown window for asp.net level forced shutdown.
            // This time may be used to tidy up user states and update the database to a sane state (ie. marking open multiplayer
            // rooms as closed).
            services.Configure<HostOptions>(opts => opts.ShutdownTimeout = GracefulShutdownManager.TIME_BEFORE_FORCEFUL_SHUTDOWN.Add(TimeSpan.FromMinutes(1)));

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
                    // options will be injected through DI, via the singleton registration above.
                    .AddJwtBearer(ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME)
                    .AddJwtBearer(ConfigureJwtBearerOptions.REFEREE_CLIENT_SCHEME)
                    .AddPolicyScheme(JwtBearerDefaults.AuthenticationScheme, displayName: null, options =>
                    {
                        options.ForwardDefaultSelector = ctx => ctx.GetEndpoint()?.Metadata.GetMetadata<HubMetadata>()?.HubType == typeof(RefereeHub)
                            ? ConfigureJwtBearerOptions.REFEREE_CLIENT_SCHEME
                            : ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME;
                    });
            services.AddAuthorization(options =>
            {
                options.AddPolicy(ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scopes", "*");
                });
                options.AddPolicy(ConfigureJwtBearerOptions.REFEREE_CLIENT_SCHEME, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scopes", "multiplayer.write_manage");
                });
            });
            services.AddSingleton<IUserIdProvider, JwtUserIdProvider>();
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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<SpectatorHub>("/spectator");
                endpoints.MapHub<MultiplayerHub>("/multiplayer");
                endpoints.MapHub<MetadataHub>("/metadata");
                endpoints.MapHub<RefereeHub>("/referee");
            });

            // add serving static files for the sake of docs.
            // it's a good idea to keep these statement *after* `UseEndpoints()`.
            // this is because the order of invocations here matters - methods invoked earlier take precedence in ASP.NET's middleware pipeline.
            // therefore this running *after* `UseEndpoints()` should ensure that actual application use cases take precedence over any docs concerns.
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(env.WebRootPath, "docs")),
                RequestPath = "/docs"
            });

            // Create shutdown manager singleton.
            // Importantly, this has to be after the endpoint initialisation due to LIFO firing of `ApplicationStopping`.
            // If this is not done last, signalr will clean up connections before the manager has a chance to gracefully wait for
            // usage to finish.
            // See https://github.com/dotnet/aspnetcore/issues/25069#issuecomment-912817907
            app.ApplicationServices.GetRequiredService<GracefulShutdownManager>();

            app.ApplicationServices.GetRequiredService<MetadataBroadcaster>();
            app.ApplicationServices.GetRequiredService<ScoreUploader>();
            app.ApplicationServices.GetRequiredService<BuildUserCountUpdater>();
        }
    }
}
