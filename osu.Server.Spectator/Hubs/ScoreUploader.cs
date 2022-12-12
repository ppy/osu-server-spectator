// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
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
    public class ScoreUploader : IDisposable
    {
        /// <summary>
        /// A timeout to drop scores which haven't had IDs assigned to their tokens.
        /// This can happen if the user forcefully terminated the game before the API score submission request is sent, but after EndPlaySession() has been invoked.
        /// </summary>
        private const int timeout_seconds = 30;

        private bool shouldSaveReplays => Environment.GetEnvironmentVariable("SAVE_REPLAYS") == "1";

        private readonly ConcurrentQueue<UploadItem> queue = new ConcurrentQueue<UploadItem>();
        private readonly IDatabaseFactory databaseFactory;
        private readonly Timer timer;

        public ScoreUploader(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            timer = new Timer(5000);
            timer.AutoReset = false;
            timer.Elapsed += update;
            timer.Start();
        }

        public void Enqueue(long token, Score score)
        {
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
                    while (queue.TryDequeue(out var item))
                    {
                        long? scoreId = await db.GetScoreIdFromToken(item.Token);

                        if (scoreId == null)
                        {
                            if (item.Cancellation.IsCancellationRequested)
                            {
                                Console.WriteLine($"Score upload timed out for token: {item.Token}");
                                item.Dispose();
                                continue;
                            }

                            // Score is not ready yet.
                            queue.Enqueue(item);
                        }
                        else
                        {
                            await uploadScore(scoreId.Value, item);
                            item.Dispose();
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
                timer.Start();
            }
        }

        private async Task uploadScore(long scoreId, UploadItem item)
        {
            if (!shouldSaveReplays)
                return;

            var scoreInfo = item.Score.ScoreInfo;
            var legacyEncoder = new LegacyScoreEncoder(item.Score, null);

            string path = Path.Combine(SpectatorHub.REPLAYS_PATH, scoreInfo.Date.Year.ToString(), scoreInfo.Date.Month.ToString(), scoreInfo.Date.Day.ToString());

            Directory.CreateDirectory(path);

            string filename = $"replay-{scoreInfo.Ruleset.ShortName}_{scoreInfo.BeatmapInfo.OnlineID}_{scoreId}.osr";

            Console.WriteLine($"Writing replay for score {scoreId} to {filename}");

            using (var outStream = File.Create(Path.Combine(path, filename)))
                legacyEncoder.Encode(outStream);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }

        private record UploadItem(long Token, Score Score, CancellationTokenSource Cancellation) : IDisposable
        {
            public void Dispose()
            {
                Cancellation.Dispose();
            }
        }
    }
}
