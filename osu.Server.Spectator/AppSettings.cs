// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator
{
    public static class AppSettings
    {
        public static bool SaveReplays { get; set; }
        public static int ReplayUploaderConcurrency { get; set; }

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

        public static string SharedInteropDomain { get; }
        public static string SharedInteropSecret { get; }

        public static string? SentryDsn { get; }

        public static int BanchoBotUserId { get; }

        public static int MatchmakingRoomSize { get; set; }
        public static int MatchmakingRoomRounds { get; set; }
        public static bool MatchmakingRoomAllowSkip { get; set; }
        public static TimeSpan MatchmakingLobbyUpdateRate { get; }
        public static TimeSpan MatchmakingQueueUpdateRate { get; }

        /// <summary>
        /// The initial rating search radius.
        /// </summary>
        /// <remarks>
        /// Defaults to 20.
        /// </remarks>
        public static double MatchmakingRatingInitialRadius { get; } = 20;

        /// <summary>
        /// The amount of time (in seconds) before each doubling of the rating search radius.
        /// </summary>
        /// <remarks>
        /// Defaults to doubling every 15 seconds. After 90 seconds it will cover all possible users.
        /// </remarks>
        public static double MatchmakingRatingRadiusIncreaseTime { get; } = 15;

        static AppSettings()
        {
            SaveReplays = Environment.GetEnvironmentVariable("SAVE_REPLAYS") == "1";
            ReplayUploaderConcurrency = int.Parse(Environment.GetEnvironmentVariable("REPLAY_UPLOAD_THREADS") ?? "1");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReplayUploaderConcurrency);

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

            SharedInteropDomain = Environment.GetEnvironmentVariable("SHARED_INTEROP_DOMAIN") ?? "http://localhost:8080";
            SharedInteropSecret = Environment.GetEnvironmentVariable("SHARED_INTEROP_SECRET") ?? string.Empty;

            SentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN");

            BanchoBotUserId = int.TryParse(Environment.GetEnvironmentVariable("BANCHO_BOT_USER_ID"), out int id) ? id : 3;

            MatchmakingRoomSize = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ROOM_SIZE"), out int mmSize) ? mmSize : 8;
            MatchmakingRoomRounds = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ROOM_ROUNDS"), out int mmRounds) ? mmRounds : 8;
            MatchmakingRoomAllowSkip = bool.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ALLOW_SKIP"), out bool mmAllowSkip) && mmAllowSkip;
            MatchmakingLobbyUpdateRate = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_LOBBY_UPDATE_RATE"), out int mmLobbyUpdateRate)
                ? TimeSpan.FromSeconds(mmLobbyUpdateRate)
                : TimeSpan.FromSeconds(5);
            MatchmakingQueueUpdateRate = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_QUEUE_UPDATE_RATE"), out int mmQueueUpdateRate)
                ? TimeSpan.FromSeconds(mmQueueUpdateRate)
                : TimeSpan.FromSeconds(1);
            MatchmakingRatingInitialRadius = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_RATING_INITIAL_RADIUS"), out int mmRatingInitialRadius)
                ? mmRatingInitialRadius
                : MatchmakingRatingInitialRadius;
            MatchmakingRatingRadiusIncreaseTime = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_RATING_RADIUS_INCREASE_TIME"), out int mmRatingRadiusIncreaseTime)
                ? mmRatingRadiusIncreaseTime
                : MatchmakingRatingRadiusIncreaseTime;
        }
    }
}
