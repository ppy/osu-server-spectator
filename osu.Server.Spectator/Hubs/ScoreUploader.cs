// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Storage;
using StatsdClient;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreUploader : IEntityStore, IDisposable
    {
        /// <summary>
        /// Amount of time (in milliseconds) before any individual score times out if a score ID hasn't been set.
        /// This can happen if the user forcefully terminated the game before the API score submission request is sent, but after EndPlaySession() has been invoked.
        /// </summary>
        public double TimeoutInterval = 30000;

        public bool SaveReplays = AppSettings.SaveReplays;

        private const string statsd_prefix = "score_uploads";

        private readonly Channel<UploadItem> channel = Channel.CreateUnbounded<UploadItem>();

        private readonly IDatabaseFactory databaseFactory;
        private readonly IScoreStorage scoreStorage;
        private readonly IMemoryCache memoryCache;
        private readonly CancellationTokenSource cancellationSource;
        private readonly CancellationToken cancellationToken;
        private readonly ILogger logger;

        public ScoreUploader(
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            IScoreStorage scoreStorage,
            IMemoryCache memoryCache)
        {
            this.databaseFactory = databaseFactory;
            this.scoreStorage = scoreStorage;
            this.memoryCache = memoryCache;
            logger = loggerFactory.CreateLogger(nameof(ScoreUploader));

            cancellationSource = new CancellationTokenSource();
            cancellationToken = cancellationSource.Token;

            for (int i = 0; i < AppSettings.ReplayUploaderConcurrency; ++i)
                Task.Factory.StartNew(readLoop, TaskCreationOptions.LongRunning);

            Task.Factory.StartNew(monitorLoop, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Enqueues a new score to be uploaded.
        /// </summary>
        /// <param name="token">The score's token.</param>
        /// <param name="score">The score.</param>
        /// <param name="beatmap">The beatmap on which the score was set.</param>
        public async Task EnqueueAsync(long token, Score score, database_beatmap beatmap)
        {
            if (!SaveReplays)
                return;

            Interlocked.Increment(ref remainingUsages);

            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(TimeoutInterval));

            await channel.Writer.WriteAsync(new UploadItem(token, score, beatmap, cancellation), cancellationToken);
        }

        private async Task readLoop()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var db = databaseFactory.GetInstance();

                var item = await channel.Reader.ReadAsync(cancellationToken);
                bool itemProcessed = true;

                try
                {
                    SoloScore? dbScore = await db.GetScoreFromTokenAsync(item.Token);

                    if (dbScore == null && !item.Cancellation.IsCancellationRequested)
                    {
                        // Score is not ready yet - enqueue for the next attempt.
                        await channel.Writer.WriteAsync(item, cancellationToken);
                        itemProcessed = false;
                        continue;
                    }

                    if (dbScore == null)
                    {
                        logger.LogError("Score upload timed out for token: {tokenId}", item.Token);
                        DogStatsd.Increment($"{statsd_prefix}.timed_out");
                        continue;
                    }

                    if (!dbScore.passed)
                        continue;

                    if (dbScore.build_id != null)
                    {
                        var build = await memoryCache.GetOrCreateAsync($"{nameof(osu_build)}#{dbScore.build_id}",
                            async _ =>
                            {
                                using (var conn = databaseFactory.GetInstance())
                                    return await conn.GetBuildByIdAsync(dbScore.build_id.Value);
                            });
                        item.Score.ScoreInfo.ClientVersion = build?.version ?? string.Empty;
                    }

                    item.Score.ScoreInfo.OnlineID = (long)dbScore.id;
                    item.Score.ScoreInfo.Passed = dbScore.passed;

                    await scoreStorage.WriteAsync(item);
                    await db.MarkScoreHasReplay(item.Score);
                    DogStatsd.Increment($"{statsd_prefix}.uploaded");
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error during score upload");
                    DogStatsd.Increment($"{statsd_prefix}.failed");
                }
                finally
                {
                    if (itemProcessed)
                    {
                        item.Dispose();
                        Interlocked.Decrement(ref remainingUsages);
                    }
                }
            }
        }

        private async Task monitorLoop()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DogStatsd.Gauge($"{statsd_prefix}.total_in_queue", remainingUsages);
                await Task.Delay(1000, cancellationToken);
            }
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }

        public record UploadItem(long Token, Score Score, database_beatmap Beatmap, CancellationTokenSource Cancellation) : IDisposable
        {
            public void Dispose()
            {
                Cancellation.Dispose();
            }
        }

        private int remainingUsages;

        // Using the count of items in the queue isn't correct since items are dequeued for processing.
        public int RemainingUsages => remainingUsages;

        public string EntityName => "Score uploads";

        public void StopAcceptingEntities()
        {
            // Handled by the spectator hub.
        }
    }
}
