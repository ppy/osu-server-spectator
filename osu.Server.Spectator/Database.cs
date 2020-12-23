// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using MySqlConnector;

namespace osu.Server.Spectator
{
    public class Database
    {
        public static MySqlConnection GetConnection()
        {
            string host = (Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost");
            string user = (Environment.GetEnvironmentVariable("DB_USER") ?? "root");

            DapperExtensions.InstallDateTimeOffsetMapper();

            var connection = new MySqlConnection($"Server={host};Database=osu;User ID={user};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;");
            connection.Open();
            return connection;
        }
    }
}
