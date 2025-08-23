// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using osu.Game.Online;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Hubs.Metadata;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Spectator;
using Dapper;

namespace osu.Server.Spectator
{
    // 添加 TypeHandler 类
    public class PlaymodeTypeHandler : SqlMapper.TypeHandler<ushort>
    {
        public override void SetValue(IDbDataParameter parameter, ushort value)
        {
            parameter.Value = value;
        }

        public override ushort Parse(object value)
        {
            if (value is string stringValue)
            {
                return stringValue.ToUpper() switch
                {
                    "OSU" => 0,
                    "TAIKO" => 1,
                    "CATCH" => 2,
                    "FRUITS" => 2, // 别名
                    "MANIA" => 3,
                    _ => ushort.TryParse(stringValue, out var result) ? result : (ushort)0
                };
            }
            
            if (value is ushort ushortValue)
                return ushortValue;
                
            if (value is int intValue)
                return (ushort)intValue;
                
            return Convert.ToUInt16(value);
        }
    }

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR(options =>
                    {
                        options.AddFilter<LoggingHubFilter>();
                        options.AddFilter<ConcurrentConnectionLimiter>();
                    })
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
                    .AddNewtonsoftJsonProtocol(options =>
                    {
                        options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;

                        // This is required to make derived class serialisation work.
                        // It is a safe alternative to `TypeNameHandling.Auto`, and the only viable solution
                        // due to various means of making `TypeNameHandling.Auto` safe not working on root classes.
                        options.PayloadSerializerSettings.Converters = new List<JsonConverter>
                        {
                            new SignalRDerivedTypeWorkaroundJsonConverter(),
                        };
                    });

            services.AddHubEntities()
                    .AddDatabaseServices()
                    .AddMemoryCache();

            // 在这里注册 Dapper TypeHandler
            SqlMapper.AddTypeHandler(new PlaymodeTypeHandler());

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
                    .AddJwtBearer(); // options will be injected through DI, via the singleton registration above.
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