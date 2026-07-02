// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Extensions;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using StatsdClient;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreBuffer : IEntityStore, IDisposable
    {
        /// <summary>
        /// The amount of time after which a score can be dropped from the buffer
        /// because the relevant client updating its data is considered to be permanently dead.
        /// Note that:
        /// <list type="bullet">
        /// <item>The client is expected to <see cref="UpdateAsync"/> a score's data at most every <see cref="SpectatorClient.TIME_BETWEEN_SENDS"/> ms.</item>
        /// <item>Every successful invocation of <see cref="UpdateAsync"/> bumps a score's last update date, therefore resetting its expiry timeout to zero.</item>
        /// <item>
        /// As per <see href="https://learn.microsoft.com/en-us/aspnet/signalr/overview/guide-to-the-api/handling-connection-lifetime-events#timeout-and-keepalive-settings">default timeout
        /// and keepalive settings</see>, unless some of the values mentioned in the above link are tweaked, timeouts lower than 110 seconds probably do not make sense at all ever.
        /// </item>
        /// </list>
        /// </summary>
        public double TimeoutInterval = 300_000; // ms = 5 min

        private const string statsd_prefix = "score_buffer";

        private readonly EntityStore<BufferedScore> store;
        private readonly CancellationTokenSource expiryLoopCancellation;

        public ScoreBuffer(EntityStore<BufferedScore> store)
        {
            this.store = store;

            expiryLoopCancellation = new CancellationTokenSource();
            Task.Factory.StartNew(expiryLoop, TaskCreationOptions.LongRunning);
        }

        public async Task<bool> TryAddAsync(long scoreTokenId, Score score, database_beatmap beatmap)
        {
            using (var usage = await store.GetForUse(scoreTokenId, createOnMissing: true))
            {
                if (usage.Item != null)
                    return false;

                usage.Item = new BufferedScore(score, beatmap);
                return true;
            }
        }

        public async Task UpdateAsync(long scoreTokenId, FrameDataBundle data)
        {
            using (var usage = await store.TryGetForUse(scoreTokenId))
            {
                if (usage == null)
                    return;

                var buffered = usage.Item;
                Debug.Assert(buffered != null);

                buffered.Score.ScoreInfo.Accuracy = data.Header.Accuracy;
                buffered.Score.ScoreInfo.Statistics = data.Header.Statistics;
                buffered.Score.ScoreInfo.MaxCombo = data.Header.MaxCombo;
                buffered.Score.ScoreInfo.Combo = data.Header.Combo;
                buffered.Score.ScoreInfo.TotalScore = data.Header.TotalScore;
                buffered.Score.ScoreInfo.APIMods = data.Header.Mods;

                // handle frame bundles from old clients that don't send both of these properties
                // null checks can be elided when property is made non-nullable on `FrameDataBundle` 20261126
                if (data.Header.TotalScoreWithoutMods != null)
                    buffered.Score.ScoreInfo.TotalScoreWithoutMods = data.Header.TotalScoreWithoutMods.Value;

                if (data.Header.Pauses != null)
                {
                    buffered.Score.ScoreInfo.Pauses.Clear();
                    buffered.Score.ScoreInfo.Pauses.AddRange(data.Header.Pauses);
                }

                buffered.Score.Replay.Frames.AddRange(data.Frames);

                buffered.LastUpdated = DateTimeOffset.Now;
            }
        }

        public async Task<BufferedScore?> DequeueAsync(long scoreTokenId)
        {
            using (var usage = await store.TryGetForUse(scoreTokenId))
            {
                if (usage == null)
                    return null;

                var buffered = usage.Item;
                Debug.Assert(buffered != null);

                usage.Destroy();
                DogStatsd.Increment($@"{statsd_prefix}.dequeued");
                return buffered;
            }
        }

        private async Task expiryLoop()
        {
            while (!expiryLoopCancellation.IsCancellationRequested)
            {
                var threshold = DateTimeOffset.Now.AddMilliseconds(-TimeoutInterval);

                var expiredScoreTokens = store.GetAllEntities()
                                              .Where(kv => kv.Value.LastUpdated < threshold)
                                              .Select(kv => kv.Key)
                                              .ToList();

                foreach (long scoreToken in expiredScoreTokens)
                {
                    using (var usage = await store.TryGetForUse(scoreToken))
                    {
                        if (usage?.Item == null || usage.Item.LastUpdated < threshold)
                        {
                            usage?.Destroy();
                            DogStatsd.Increment($@"{statsd_prefix}.expired");
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5000));
            }
        }

        public class BufferedScore
        {
            public Score Score { get; }
            public database_beatmap Beatmap { get; }
            public DateTimeOffset LastUpdated { get; set; }

            public BufferedScore(Score score, database_beatmap beatmap)
            {
                Score = score;
                Beatmap = beatmap;
                LastUpdated = DateTimeOffset.Now;
            }
        }

        public void Dispose()
        {
            expiryLoopCancellation.Cancel();
            expiryLoopCancellation.Dispose();
        }

        public int RemainingUsages => store.RemainingUsages;
        public string EntityName => "Buffered partial scores";
        public void StopAcceptingEntities() => store.StopAcceptingEntities();
    }
}
