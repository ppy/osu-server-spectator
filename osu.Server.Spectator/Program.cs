// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace osu.Server.Spectator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
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

                           webBuilder.UseUrls(urls: new[] { "http://*:80", "https://*:443" });
                       });
        }
    }
}
