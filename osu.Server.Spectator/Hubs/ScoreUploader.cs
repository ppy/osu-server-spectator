// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreUploader : IDisposable
    {
        // private bool shouldSaveReplays => Environment.GetEnvironmentVariable("SAVE_REPLAYS") == "1";

        private readonly ConcurrentQueue<(long token, Score score)> queue = new ConcurrentQueue<(long token, Score score)>();
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

        public void Enqueue(long token, Score score) => queue.Enqueue((token, score));

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
                        long? scoreId = await db.GetScoreIdFromToken(item.token);

                        if (scoreId == null)
                        {
                            // Score is not ready yet.
                            queue.Enqueue(item);
                        }
                        else
                        {
                            await uploadScore(scoreId.Value, item.score);
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

        private async Task uploadScore(long scoreId, Score score)
        {
            var now = DateTimeOffset.UtcNow;

            score.ScoreInfo.Date = now;
            var legacyEncoder = new LegacyScoreEncoder(score, null);

            string path = Path.Combine(SpectatorHub.REPLAYS_PATH, now.Year.ToString(), now.Month.ToString(), now.Day.ToString());

            Directory.CreateDirectory(path);

            string filename = $"replay-{score.ScoreInfo.Ruleset.ShortName}_{score.ScoreInfo.BeatmapInfo.OnlineID}_{scoreId}.osr";

            Console.WriteLine($"Writing replay for score {scoreId} to {filename}");

            using (var outStream = File.Create(Path.Combine(path, filename)))
                legacyEncoder.Encode(outStream);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }
    }
}
