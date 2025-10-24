// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator
{
    public static class AppSettings
    {
        public static bool SaveReplays { get; set; }
        public static int ReplayUploaderConcurrency { get; set; } = 1;

        #region For use with FileScoreStorage

        public static string ReplaysPath { get; set; } = "replays";

        #endregion

        #region For use with S3ScoreStorage

        public static string S3Key { get; } = string.Empty;
        public static string S3Secret { get; } = string.Empty;
        public static string ReplaysBucket { get; } = string.Empty;

        #endregion

        public static bool TrackBuildUserCounts { get; set; }

        public static int ServerPort { get; set; } = 80;
        public static string RedisHost { get; } = "localhost";
        public static string DataDogAgentHost { get; set; } = "localhost";

        public static string DatabaseHost { get; } = "localhost";
        public static string DatabaseUser { get; } = "osuweb";
        public static int DatabasePort { get; } = 3306;

        public static string SharedInteropDomain { get; } = "http://localhost:8080";
        public static string SharedInteropSecret { get; } = string.Empty;

        public static string? SentryDsn { get; }

        public static int BanchoBotUserId { get; } = 3;

        public static int MatchmakingRoomSize { get; set; } = 8;
        public static int MatchmakingRoomRounds { get; set; } = 5;
        public static bool MatchmakingRoomAllowSkip { get; set; }
        public static TimeSpan MatchmakingLobbyUpdateRate { get; } = TimeSpan.FromSeconds(5);
        public static TimeSpan MatchmakingQueueUpdateRate { get; } = TimeSpan.FromSeconds(1);

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

        /// <summary>
        /// The total number of beatmaps per matchmaking room.
        /// </summary>
        public static int MatchmakingPoolSize { get; set; } = 50;

        static AppSettings()
        {
            SaveReplays = bool.TryParse(Environment.GetEnvironmentVariable("SAVE_REPLAYS"), out bool saveReplays) ? saveReplays : SaveReplays;
            ReplayUploaderConcurrency = int.TryParse(Environment.GetEnvironmentVariable("REPLAY_UPLOAD_THREADS"), out int uploaderConcurrency) ? uploaderConcurrency : ReplayUploaderConcurrency;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReplayUploaderConcurrency);

            ReplaysPath = Environment.GetEnvironmentVariable("REPLAYS_PATH") ?? ReplaysPath;
            S3Key = Environment.GetEnvironmentVariable("S3_KEY") ?? S3Key;
            S3Secret = Environment.GetEnvironmentVariable("S3_SECRET") ?? S3Secret;
            ReplaysBucket = Environment.GetEnvironmentVariable("REPLAYS_BUCKET") ?? ReplaysBucket;
            TrackBuildUserCounts = bool.TryParse(Environment.GetEnvironmentVariable("TRACK_BUILD_USER_COUNTS"), out bool trackBuildUserCounts) ? trackBuildUserCounts : TrackBuildUserCounts;

            ServerPort = int.TryParse(Environment.GetEnvironmentVariable("SERVER_PORT"), out int serverPort) ? serverPort : ServerPort;
            RedisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? RedisHost;
            DataDogAgentHost = Environment.GetEnvironmentVariable("DD_AGENT_HOST") ?? DataDogAgentHost;

            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? DatabaseHost;
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? DatabaseUser;
            DatabasePort = int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out int databasePort) ? databasePort : DatabasePort;

            SharedInteropDomain = Environment.GetEnvironmentVariable("SHARED_INTEROP_DOMAIN") ?? SharedInteropDomain;
            SharedInteropSecret = Environment.GetEnvironmentVariable("SHARED_INTEROP_SECRET") ?? SharedInteropSecret;

            SentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN");

            BanchoBotUserId = int.TryParse(Environment.GetEnvironmentVariable("BANCHO_BOT_USER_ID"), out int banchoBotUserId) ? banchoBotUserId : BanchoBotUserId;

            MatchmakingRoomSize = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ROOM_SIZE"), out int mmSize)
                ? mmSize
                : MatchmakingRoomSize;

            MatchmakingRoomRounds = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ROOM_ROUNDS"), out int mmRounds)
                ? mmRounds
                : MatchmakingRoomRounds;

            MatchmakingRoomAllowSkip = bool.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_ALLOW_SKIP"), out bool mmAllowSkip)
                ? mmAllowSkip
                : MatchmakingRoomAllowSkip;

            MatchmakingLobbyUpdateRate = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_LOBBY_UPDATE_RATE"), out int mmLobbyUpdateRate)
                ? TimeSpan.FromSeconds(mmLobbyUpdateRate)
                : MatchmakingLobbyUpdateRate;

            MatchmakingQueueUpdateRate = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_QUEUE_UPDATE_RATE"), out int mmQueueUpdateRate)
                ? TimeSpan.FromSeconds(mmQueueUpdateRate)
                : MatchmakingQueueUpdateRate;

            MatchmakingRatingInitialRadius = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_RATING_INITIAL_RADIUS"), out int mmRatingInitialRadius)
                ? mmRatingInitialRadius
                : MatchmakingRatingInitialRadius;

            MatchmakingRatingRadiusIncreaseTime = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_RATING_RADIUS_INCREASE_TIME"), out int mmRatingRadiusIncreaseTime)
                ? mmRatingRadiusIncreaseTime
                : MatchmakingRatingRadiusIncreaseTime;

            MatchmakingPoolSize = int.TryParse(Environment.GetEnvironmentVariable("MATCHMAKING_POOL_SIZE"), out int mmPoolSize)
                ? mmPoolSize
                : MatchmakingPoolSize;
        }
    }
}
