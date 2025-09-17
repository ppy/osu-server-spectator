// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator
{
    public static class AppSettings
    {
        public static bool SaveReplays { get; set; }
        public static int ReplayUploaderConcurrency { get; set; }

        #region Sync With g0v0-server

        public static bool EnableAllBeatmapLeaderboard { get; set; }

        // ReSharper disable once InconsistentNaming
        public static bool EnableAP { get; set; }

        // ReSharper disable once InconsistentNaming
        public static bool EnableRX { get; set; }

        #endregion

        public static bool TrackBuildUserCounts { get; set; }

        public static string ServerPort { get; set; }
        public static string RedisHost { get; }
        public static string DataDogAgentHost { get; set; }

        public static string DatabaseHost { get; }
        public static string DatabaseUser { get; }
        public static string DatabasePassword { get; }
        public static string DatabaseName { get; }
        public static string DatabasePort { get; }

        public static string SharedInteropDomain { get; }
        public static string SharedInteropSecret { get; }

        public static string? SentryDsn { get; }

        #region JWT Authentication Settings

        public static string JwtSecretKey { get; }
        public static string JwtAlgorithm { get; }
        public static int JwtAccessTokenExpireMinutes { get; }
        public static int OsuClientId { get; }
        public static bool UseLegacyRsaAuth { get; }

        #endregion

        static AppSettings()
        {
            SaveReplays = Environment.GetEnvironmentVariable("SAVE_REPLAYS") == "1";
            ReplayUploaderConcurrency = int.Parse(Environment.GetEnvironmentVariable("REPLAY_UPLOAD_THREADS") ?? "1");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReplayUploaderConcurrency);

            EnableAllBeatmapLeaderboard = Environment.GetEnvironmentVariable("ENABLE_ALL_BEATMAP_LEADERBOARD") == "true";
            EnableAP = (Environment.GetEnvironmentVariable("ENABLE_AP") ?? Environment.GetEnvironmentVariable("ENABLE_OSU_AP")) == "true";
            EnableRX = (Environment.GetEnvironmentVariable("ENABLE_RX") ?? Environment.GetEnvironmentVariable("ENABLE_OSU_RX")) == "true";

            TrackBuildUserCounts = Environment.GetEnvironmentVariable("TRACK_BUILD_USER_COUNTS") == "1";

            ServerPort = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "8086";
            RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            DataDogAgentHost = Environment.GetEnvironmentVariable("DD_AGENT_HOST") ?? "localhost";

            DatabaseHost = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
            DatabaseUser = Environment.GetEnvironmentVariable("MYSQL_USER") ?? Environment.GetEnvironmentVariable("DB_USER") ?? "osu_api";
            DatabasePassword = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "osu_password";
            DatabaseName = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? Environment.GetEnvironmentVariable("DB_NAME") ?? "osu_api";
            DatabasePort = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";

            SharedInteropDomain = Environment.GetEnvironmentVariable("SHARED_INTEROP_DOMAIN") ?? "http://localhost:8000";
            SharedInteropSecret = Environment.GetEnvironmentVariable("SHARED_INTEROP_SECRET") ?? string.Empty;

            SentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN") ?? null;

            // JWT Authentication Settings
            JwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? "your_jwt_secret_here";
            JwtAlgorithm = Environment.GetEnvironmentVariable("JWT_ALGORITHM") ?? "HS256";
            JwtAccessTokenExpireMinutes = int.Parse(Environment.GetEnvironmentVariable("JWT_ACCESS_TOKEN_EXPIRE_MINUTES") ?? "1440");
            OsuClientId = int.Parse(Environment.GetEnvironmentVariable("OSU_CLIENT_ID") ?? "5");
            UseLegacyRsaAuth = Environment.GetEnvironmentVariable("USE_LEGACY_RSA_AUTH") == "1";
        }
    }
}
