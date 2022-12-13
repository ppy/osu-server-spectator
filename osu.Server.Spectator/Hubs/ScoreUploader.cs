// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Server.Spectator.Database;
using Timer = System.Timers.Timer;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreUploader : IScoreUploader, IDisposable
    {
        /// <summary>
        /// A timeout to drop scores which haven't had IDs assigned to their tokens.
        /// This can happen if the user forcefully terminated the game before the API score submission request is sent, but after EndPlaySession() has been invoked.
        /// </summary>
        private const int timeout_seconds = 30;

        private readonly ConcurrentQueue<UploadItem> queue = new ConcurrentQueue<UploadItem>();
        private readonly IDatabaseFactory databaseFactory;
        private readonly Timer timer;
        private readonly CancellationTokenSource timerCancellationSource;
        private readonly CancellationToken timerCancellationToken;

        public ScoreUploader(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            timerCancellationSource = new CancellationTokenSource();
            timerCancellationToken = timerCancellationSource.Token;

            timer = new Timer(5000);
            timer.AutoReset = false;
            timer.Elapsed += update;
            timer.Start();
        }

        public void Enqueue(long token, Score score)
        {
            Interlocked.Increment(ref remainingUsages);

            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromSeconds(timeout_seconds));

            queue.Enqueue(new UploadItem(token, score, cancellation));
        }

        private async void update(object? sender, ElapsedEventArgs args)
        {
            try
            {
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

                        using (item)
                        {
                            if (scoreId != null)
                                await uploadScore(scoreId.Value, item);
                            else
                                Console.WriteLine($"Score upload timed out for token: {item.Token}");

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

        private async Task uploadScore(long scoreId, UploadItem item)
        {
            if (!AppSettings.SaveReplays)
                return;

            using (var outStream = new MemoryStream())
            {
                new LegacyScoreEncoder(item.Score, null).Encode(outStream, true);

                outStream.Seek(0, SeekOrigin.Begin);

                Console.WriteLine($"Uploading replay for score {scoreId}");

                S3.Upload(AppSettings.ReplaysBucket, scoreId.ToString(CultureInfo.InvariantCulture), outStream, outStream.Length);
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

        public int RemainingUsages => remainingUsages;

        public string EntityName => "Score uploads";

        public void StopAcceptingEntities()
        {
            // Handled by the spectator hub.
        }
    }
}
