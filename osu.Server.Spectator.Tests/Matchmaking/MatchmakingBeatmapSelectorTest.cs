// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingBeatmapSelectorTest
    {
        [Fact]
        public void OnlyEasyBeatmaps()
        {
            MatchmakingBeatmapSelector beatmapSelector = new MatchmakingBeatmapSelector(
                Enumerable.Range(1, 1000).Select(i => new matchmaking_pool_beatmap
                {
                    id = i,
                    rating = 1000,
                }).ToArray())
            {
                PoolSize = 50
            };

            matchmaking_pool_beatmap[] result = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.Equal(50, result.Length);

            matchmaking_pool_beatmap[] result2 = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.NotEqual(result.OrderBy(b => b.id), result2.OrderBy(b => b.id));
        }

        [Fact]
        public void OnlyAverageBeatmaps()
        {
            MatchmakingBeatmapSelector beatmapSelector = new MatchmakingBeatmapSelector(
                Enumerable.Range(1, 1000).Select(i => new matchmaking_pool_beatmap
                {
                    id = i,
                    rating = 1500,
                }).ToArray())
            {
                PoolSize = 50
            };

            matchmaking_pool_beatmap[] result = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.Equal(50, result.Length);

            matchmaking_pool_beatmap[] result2 = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.NotEqual(result.OrderBy(b => b.id), result2.OrderBy(b => b.id));
        }

        [Fact]
        public void OnlyHardBeatmaps()
        {
            MatchmakingBeatmapSelector beatmapSelector = new MatchmakingBeatmapSelector(
                Enumerable.Range(1, 1000).Select(i => new matchmaking_pool_beatmap
                {
                    id = i,
                    rating = 2000,
                }).ToArray())
            {
                PoolSize = 50
            };

            matchmaking_pool_beatmap[] result = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.Equal(50, result.Length);

            matchmaking_pool_beatmap[] result2 = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.NotEqual(result.OrderBy(b => b.id), result2.OrderBy(b => b.id));
        }

        [Fact]
        public void WideSelection()
        {
            MatchmakingBeatmapSelector beatmapSelector = new MatchmakingBeatmapSelector(
                Enumerable.Range(1, 1000).Select(i => new matchmaking_pool_beatmap
                {
                    id = i,
                    rating = 1000 + i,
                }).ToArray())
            {
                PoolSize = 50
            };

            matchmaking_pool_beatmap[] result = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            int countEasy = result.Count(b => b.rating < 1500);
            int countHard = result.Count(b => b.rating > 1500);

            Assert.Equal(50, result.Length);
            Assert.True((double)countEasy / result.Length >= 0.25);
            Assert.True((double)countHard / result.Length >= 0.25);

            matchmaking_pool_beatmap[] result2 = beatmapSelector.GetAppropriateBeatmaps([new EloRating(1500, 80)]);
            Assert.NotEqual(result.OrderBy(b => b.id), result2.OrderBy(b => b.id));
        }

        [Fact]
        public void NoDuplicates()
        {
            MatchmakingBeatmapSelector selector = new MatchmakingBeatmapSelector(
            [
                new matchmaking_pool_beatmap { id = 0, rating = 1000 },
                new matchmaking_pool_beatmap { id = 1, rating = 1000 },
            ]);

            matchmaking_pool_beatmap[] result = selector.GetAppropriateBeatmaps([new EloRating(1000, 350), new EloRating(1000, 350)]);

            Assert.Equal(2, result.Length);
            Assert.Single(result, t => t.id == 0);
            Assert.Single(result, t => t.id == 1);
        }
    }
}
