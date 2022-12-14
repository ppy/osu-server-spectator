// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Storage;
using Timer = System.Timers.Timer;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreUploader : IEntityStore, IDisposable
    {
        /// <summary>
        /// Amount of time (in milliseconds) between checks for pending score uploads.
        /// </summary>
        public double UploadInterval
        {
            get => timer.Interval;
            set => timer.Interval = value;
        }

        /// <summary>
        /// Amount of time (in milliseconds) before any individual score times out if a score ID hasn't been set.
        /// This can happen if the user forcefully terminated the game before the API score submission request is sent, but after EndPlaySession() has been invoked.
        /// </summary>
        public double TimeoutInterval = 30000;

        private readonly ConcurrentQueue<UploadItem> queue = new ConcurrentQueue<UploadItem>();
        private readonly IDatabaseFactory databaseFactory;
        private readonly IScoreStorage scoreStorage;
        private readonly Timer timer;
        private readonly CancellationTokenSource timerCancellationSource;
        private readonly CancellationToken timerCancellationToken;

        public ScoreUploader(IDatabaseFactory databaseFactory, IScoreStorage scoreStorage)
        {
            this.databaseFactory = databaseFactory;
            this.scoreStorage = scoreStorage;

            timerCancellationSource = new CancellationTokenSource();
            timerCancellationToken = timerCancellationSource.Token;

            timer = new Timer(5000);
            timer.AutoReset = false;
            timer.Elapsed += (_, _) => Task.Run(Flush);
            timer.Start();
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
                timer.Stop();

                if (queue.IsEmpty)
                    return;

                Console.WriteLine($"Beginning upload of {queue.Count} scores");

                using (var db = databaseFactory.GetInstance())
                {
                    int countToTry = queue.Count;

                    for (int i = 0; i < countToTry; i++)
                    {
                        if (!queue.TryDequeue(out var item))
                            continue;

                        long? scoreId = await db.GetScoreIdFromToken(item.Token);

                        if (scoreId == null && !item.Cancellation.IsCancellationRequested)
                        {
                            // Score is not ready yet - enqueue for the next attempt.
                            queue.Enqueue(item);
                            continue;
                        }

                        try
                        {
                            if (scoreId != null)
                            {
                                item.Score.ScoreInfo.OnlineID = scoreId.Value;
                                await scoreStorage.WriteAsync(item.Score);
                            }
                            else
                                Console.WriteLine($"Score upload timed out for token: {item.Token}");
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
                Console.WriteLine($"Error during score upload: {e}");
            }
            finally
            {
                if (timerCancellationToken.IsCancellationRequested)
                    timer.Dispose();
                else
                    timer.Start();
            }
        }

        public void Dispose()
        {
            timerCancellationSource.Cancel();
            timerCancellationSource.Dispose();
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
