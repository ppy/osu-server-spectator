// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreBuffer : IEntityStore
    {
        // TODO: add timed expiry of ditched scores
        // TODO: probably get some ddog observability on this. some is already provided via the inner entity store.

        private readonly EntityStore<Score> store;

        public ScoreBuffer(EntityStore<Score> store)
        {
            this.store = store;
        }

        public async Task<bool> TryAddAsync(long scoreTokenId, Score score)
        {
            using (var usage = await store.GetForUse(scoreTokenId, createOnMissing: true))
            {
                if (usage.Item != null)
                    return false;

                usage.Item = score;
                return true;
            }
        }

        public async Task UpdateAsync(long scoreTokenId, FrameDataBundle data)
        {
            using (var usage = await store.GetForUse(scoreTokenId, createOnMissing: true))
            {
                var score = usage.Item;

                if (score == null)
                {
                    // TODO: probably log or something.
                    usage.Destroy();
                    return;
                }

                score.ScoreInfo.Accuracy = data.Header.Accuracy;
                score.ScoreInfo.Statistics = data.Header.Statistics;
                score.ScoreInfo.MaxCombo = data.Header.MaxCombo;
                score.ScoreInfo.Combo = data.Header.Combo;
                score.ScoreInfo.TotalScore = data.Header.TotalScore;

                // null here means the frame bundle is from an old client that can't send mod data
                // can be removed (along with making property non-nullable on `FrameDataBundle`) 20250407
                if (data.Header.Mods != null)
                    score.ScoreInfo.APIMods = data.Header.Mods;

                score.Replay.Frames.AddRange(data.Frames);
            }
        }

        public async Task<Score?> RemoveAsync(long scoreTokenId)
        {
            using (var usage = await store.GetForUse(scoreTokenId, createOnMissing: true))
            {
                var score = usage.Item;
                usage.Destroy();
                return score;
            }
        }

        public int RemainingUsages => store.RemainingUsages;
        public string EntityName => "Buffered partial scores";
        public void StopAcceptingEntities() => store.StopAcceptingEntities();
    }
}
