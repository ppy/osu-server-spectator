// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreBuffer : IEntityStore, IDisposable
    {
        public double TimeoutInterval = 30000;

        // TODO: probably get some ddog observability on this. some is already provided via the inner entity store.

        private readonly EntityStore<BufferedScore> store;
        private readonly CancellationTokenSource expiryLoopCancellation;

        public ScoreBuffer(EntityStore<BufferedScore> store)
        {
            this.store = store;

            expiryLoopCancellation = new CancellationTokenSource();
            Task.Factory.StartNew(expiryLoop, TaskCreationOptions.LongRunning);
        }

        public async Task<bool> TryAddAsync(long scoreTokenId, Score score)
        {
            using (var usage = await store.GetForUse(scoreTokenId, createOnMissing: true))
            {
                if (usage.Item != null)
                    return false;

                usage.Item = new BufferedScore(score);
                return true;
            }
        }

        public async Task UpdateAsync(long scoreTokenId, FrameDataBundle data)
        {
            using (var usage = await store.TryGetForUse(scoreTokenId))
            {
                var buffered = usage?.Item;

                if (buffered == null)
                {
                    usage?.Destroy();
                    return;
                }

                buffered.Score.ScoreInfo.Accuracy = data.Header.Accuracy;
                buffered.Score.ScoreInfo.Statistics = data.Header.Statistics;
                buffered.Score.ScoreInfo.MaxCombo = data.Header.MaxCombo;
                buffered.Score.ScoreInfo.Combo = data.Header.Combo;
                buffered.Score.ScoreInfo.TotalScore = data.Header.TotalScore;

                // null here means the frame bundle is from an old client that can't send mod data
                // can be removed (along with making property non-nullable on `FrameDataBundle`) 20250407
                if (data.Header.Mods != null)
                    buffered.Score.ScoreInfo.APIMods = data.Header.Mods;

                buffered.Score.Replay.Frames.AddRange(data.Frames);

                buffered.LastUpdated = DateTimeOffset.Now;
            }
        }

        public async Task<Score?> RemoveAsync(long scoreTokenId)
        {
            using (var usage = await store.TryGetForUse(scoreTokenId))
            {
                var buffered = usage?.Item;
                usage?.Destroy();
                return buffered?.Score;
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
                            usage?.Destroy();
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5000));
            }
        }

        public class BufferedScore
        {
            public Score Score { get; }
            public DateTimeOffset LastUpdated { get; set; }

            public BufferedScore(Score score)
            {
                Score = score;
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
