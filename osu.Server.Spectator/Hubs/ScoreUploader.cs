// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
        /// Amount of time (in milliseconds) between checks for pending score uploads.
        /// </summary>
        public int UploadInterval { get; set; } = 50;

        /// <summary>
        /// Amount of time (in milliseconds) before any individual score times out if a score ID hasn't been set.
        /// This can happen if the user forcefully terminated the game before the API score submission request is sent, but after EndPlaySession() has been invoked.
        /// </summary>
        public double TimeoutInterval = 30000;

        private const string statsd_prefix = "score_uploads";

        private readonly ConcurrentQueue<UploadItem> queue = new ConcurrentQueue<UploadItem>();
        private readonly IDatabaseFactory databaseFactory;
        private readonly IScoreStorage scoreStorage;
        private readonly CancellationTokenSource cancellationSource;
        private readonly CancellationToken cancellationToken;
        private readonly ILogger logger;

        public ScoreUploader(
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            IScoreStorage scoreStorage)
        {
            this.databaseFactory = databaseFactory;
            this.scoreStorage = scoreStorage;
            logger = loggerFactory.CreateLogger(nameof(ScoreUploader));

            cancellationSource = new CancellationTokenSource();
            cancellationToken = cancellationSource.Token;

            Task.Factory.StartNew(runFlushLoop, TaskCreationOptions.LongRunning);
        }

        private void runFlushLoop()
        {
            while (!queue.IsEmpty || !cancellationToken.IsCancellationRequested)
            {
                // ReSharper disable once MethodSupportsCancellation
                // We don't want flush to be cancelled as it needs to finish uploading.
                Flush().Wait();
                Thread.Sleep(UploadInterval);
            }
        }

        /// <summary>
        /// Enqueues a new score to be uploaded.
        /// </summary>
        /// <param name="token">The score's token.</param>
        /// <param name="score">The score.</param>
        public void Enqueue(long token, Score score)
        {
            if (!AppSettings.SaveReplays)
                return;

            Interlocked.Increment(ref remainingUsages);

            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(TimeoutInterval));

            queue.Enqueue(new UploadItem(token, score, cancellation));
        }

        /// <summary>
        /// Flushes all pending uploads.
        /// </summary>
        public async Task Flush()
        {
            try
            {
                if (queue.IsEmpty)
                    return;

                using (var db = databaseFactory.GetInstance())
                {
                    int countToTry = queue.Count;
                    DogStatsd.Gauge($"{statsd_prefix}.total_in_queue", countToTry);

                    for (int i = 0; i < countToTry; i++)
                    {
                        if (!queue.TryDequeue(out var item))
                            continue;

                        SoloScore? dbScore = await db.GetScoreFromToken(item.Token);

                        if (dbScore == null && !item.Cancellation.IsCancellationRequested)
                        {
                            // Score is not ready yet - enqueue for the next attempt.
                            queue.Enqueue(item);
                            continue;
                        }

                        try
                        {
                            if (dbScore == null)
                            {
                                logger.LogError("Score upload timed out for token: {tokenId}", item.Token);
                                DogStatsd.Increment($"{statsd_prefix}.timed_out");
                                return;
                            }

                            if (!dbScore.passed)
                                return;

                            item.Score.ScoreInfo.OnlineID = (long)dbScore.id;
                            item.Score.ScoreInfo.Passed = dbScore.passed;

                            await scoreStorage.WriteAsync(item.Score);
                            await db.MarkScoreHasReplay(item.Score);
                            DogStatsd.Increment($"{statsd_prefix}.uploaded");
                        }
                        finally
                        {
                            item.Dispose();
                            Interlocked.Decrement(ref remainingUsages);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error during score upload");
                DogStatsd.Increment($"{statsd_prefix}.failed");
            }
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }

        private record UploadItem(long Token, Score Score, CancellationTokenSource Cancellation) : IDisposable
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
