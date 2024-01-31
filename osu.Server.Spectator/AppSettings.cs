// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator
{
    public static class AppSettings
    {
        public static bool SaveReplays { get; set; }

        #region For use with FileScoreStorage

        public static string ReplaysPath { get; set; }

        #endregion

        #region For use with S3ScoreStorage

        public static string S3Key { get; }
        public static string S3Secret { get; }
        public static string ReplaysBucket { get; }

        #endregion

        public static bool TrackBuildUserCounts { get; set; }

        public static string ServerPort { get; set; }
        public static string RedisHost { get; }
        public static string DataDogAgentHost { get; set; }

        public static string DatabaseHost { get; }
        public static string DatabaseUser { get; }
        public static string DatabasePort { get; }

        static AppSettings()
        {
            SaveReplays = Environment.GetEnvironmentVariable("SAVE_REPLAYS") == "1";
            ReplaysPath = Environment.GetEnvironmentVariable("REPLAYS_PATH") ?? "replays";
            S3Key = Environment.GetEnvironmentVariable("S3_KEY") ?? string.Empty;
            S3Secret = Environment.GetEnvironmentVariable("S3_SECRET") ?? string.Empty;
            ReplaysBucket = Environment.GetEnvironmentVariable("REPLAYS_BUCKET") ?? string.Empty;
            TrackBuildUserCounts = Environment.GetEnvironmentVariable("TRACK_BUILD_USER_COUNTS") == "1";

            ServerPort = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "80";
            RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            DataDogAgentHost = Environment.GetEnvironmentVariable("DD_AGENT_HOST") ?? "localhost";

            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "osuweb";
            DatabasePort = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
        }
    }
}
