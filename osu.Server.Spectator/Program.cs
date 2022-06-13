// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using osu.Framework.Logging;
using osu.Framework.Platform;
using Sentry;
using StatsdClient;

namespace osu.Server.Spectator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Logger.GameIdentifier = "osu-server-spectator";
            Logger.Storage = new NativeStorage(Path.Combine(Environment.CurrentDirectory, "Logs"));

            DogStatsd.Configure(new StatsdConfig
            {
                StatsdServerName = Environment.GetEnvironmentVariable("DD_AGENT_HOST") ?? "localhost",
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
                           webBuilder.UseSentry(o =>
                           {
                               o.AddExceptionFilterForType<ServerShuttingDownException>();
                               o.TracesSampleRate = 0.01;
#if !DEBUG
                               o.Dsn = "https://775dc89c1c3142e8a8fa5fd10590f443@sentry.ppy.sh/8";
#endif
                               // TODO: set release name
                           });

#if DEBUG
                           webBuilder.UseStartup<StartupDevelopment>();
#else
                           webBuilder.UseStartup<Startup>();
#endif

                           webBuilder.UseUrls(urls: new[] { "http://*:80" });
                       });
        }
    }
}
