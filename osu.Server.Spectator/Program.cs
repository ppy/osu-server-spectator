// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using osu.Framework.Logging;
using osu.Framework.Platform;
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
            });

            createHostBuilder(args).Build().Run();
        }

        private static IHostBuilder createHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                       .ConfigureWebHostDefaults(webBuilder =>
                       {
#if DEBUG
                           //todo: figure correct way to get dev environment state
                           webBuilder.UseStartup<StartupDevelopment>();
#else
                           webBuilder.UseStartup<Startup>();
#endif

                           webBuilder.UseUrls(urls: new[] { "http://*:80" });
                       });
        }
    }
}
