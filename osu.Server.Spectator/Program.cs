// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace osu.Server.Spectator
{
    public static class Program
    {
        public static MySqlConnection GetDatabaseConnection()
        {
            string host = (Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost");
            string user = (Environment.GetEnvironmentVariable("DB_USER") ?? "root");

            var connection = new MySqlConnection($"Server={host};Database=osu;User ID={user};ConnectionTimeout=1;ConnectionReset=false;Pooling=true;");
            connection.Open();
            return connection;
        }

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
                       });
        }
    }
}
