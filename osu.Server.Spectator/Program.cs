// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Net;
using Microsoft.AspNetCore.Hosting;
#if !DEBUG
using Microsoft.AspNetCore.SignalR;
#endif
using Microsoft.Extensions.Hosting;
using StatsdClient;

namespace osu.Server.Spectator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            DogStatsd.Configure(new StatsdConfig
            {
                StatsdServerName = AppSettings.DataDogAgentHost,
                Prefix = "osu.server.spectator",
                ConstantTags = new[]
                {
                    $"hostname:{Dns.GetHostName()}",
                    $"startup:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                }
            });

            createHostBuilder(args).Build().Run();
        }

        private static IHostBuilder createHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                       .ConfigureWebHostDefaults(webBuilder =>
                       {
#if !DEBUG
                           webBuilder.UseSentry(o =>
                           {
                               o.AddExceptionFilterForType<HubException>();
                               o.TracesSampleRate = 0;
                               o.Dsn = AppSettings.SentryDsn ?? throw new InvalidOperationException("SP_SENTRY_DSN environment variable not set. "
                                                                                                    + "Please set the value of this variable to a valid Sentry DSN to use for logging events.");
                               // TODO: set release name
                           });
#endif

#if DEBUG
                           webBuilder.UseStartup<StartupDevelopment>();
#else
                           webBuilder.UseStartup<Startup>();
#endif

                           webBuilder.UseUrls(urls: [$"http://*:{AppSettings.ServerPort}"]);
                       });
        }
    }
}
