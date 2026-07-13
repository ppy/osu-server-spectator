// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingBeatmapSelector
    {
        /// <summary>
        /// Contains all ranked beatmaps.
        /// </summary>
        public Dictionary<int, matchmaking_pool_beatmap> GlobalBeatmaps { get; init; } = [];

        private readonly matchmaking_pool pool;
        private readonly ConcurrentDictionary<BeatmapLookupKey, matchmaking_pool_beatmap> beatmaps;
        private readonly IDatabaseFactory dbFactory;

        private readonly ConcurrentQueue<matchmaking_pool_beatmap> pendingUpdates = [];

        public MatchmakingBeatmapSelector(matchmaking_pool pool, Dictionary<BeatmapLookupKey, matchmaking_pool_beatmap> beatmaps, IDatabaseFactory dbFactory)
        {
            this.pool = pool;
            this.beatmaps = new ConcurrentDictionary<BeatmapLookupKey, matchmaking_pool_beatmap>(beatmaps);
            this.dbFactory = dbFactory;
        }

        public MatchmakingBeatmapSelector(matchmaking_pool pool, matchmaking_pool_beatmap[] beatmaps, IDatabaseFactory dbFactory)
            : this(pool, beatmaps.ToDictionary(b => new BeatmapLookupKey(b.beatmap_id, b.mods), b => b), dbFactory)
        {
        }

        /// <summary>
        /// Creates a new <see cref="MatchmakingBeatmapSelector"/>.
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="dbFactory">The database factory.</param>
        public static async Task<MatchmakingBeatmapSelector> Initialise(matchmaking_pool pool, IDatabaseFactory dbFactory)
        {
            using (var db = dbFactory.GetInstance())
            {
                // Get all ranked beatmaps.
                Dictionary<int, matchmaking_pool_beatmap> globalBeatmaps =
                    (await db.GetMatchmakingGlobalPoolBeatmapsAsync(pool.ruleset_id, pool.variant_id))
                    .Select(b => new matchmaking_pool_beatmap
                    {
                        pool_id = pool.id,
                        beatmap_id = b.beatmap_id,
                        playmode = b.playmode,
                        checksum = b.checksum,
                        difficultyrating = b.difficultyrating,
                        rating = (int)Math.Round(800 + 500 * (Math.Exp(0.16 * b.difficultyrating) - 1)),
                    })
                    .ToDictionary(b => b.beatmap_id, b => b);

                // Get all beatmaps from the pool.
                Dictionary<BeatmapLookupKey, matchmaking_pool_beatmap> poolBeatmaps =
                    (await db.GetMatchmakingPoolBeatmapsAsync(pool.id))
                    .ToDictionary(b => new BeatmapLookupKey(b.beatmap_id, b.mods), b => b);

                // The pool may not contain all ranked beatmaps, so back-fill it.
                foreach ((int beatmapId, matchmaking_pool_beatmap beatmap) in globalBeatmaps)
                    poolBeatmaps.TryAdd(new BeatmapLookupKey(beatmapId, string.Empty), beatmap);

                return new MatchmakingBeatmapSelector(pool, poolBeatmaps, dbFactory)
                {
                    GlobalBeatmaps = globalBeatmaps
                };
            }
        }

        public async Task Update()
        {
            using (var db = dbFactory.GetInstance())
            {
                while (pendingUpdates.TryDequeue(out matchmaking_pool_beatmap? beatmap))
                    await db.UpdateMatchmakingPoolBeatmapRatingAsync(beatmap);
            }
        }

        public async Task AdjustRating(BeatmapLookupKey key, int[] playerScores, EloRating[] playerRatings)
        {
            if (AppSettings.MatchmakingDebugBeatmaps)
                return;

            // Always use the most-recent databased rating values.
            matchmaking_pool_beatmap? beatmap;
            using (var db = dbFactory.GetInstance())
                beatmap = await db.GetMatchmakingPoolBeatmapAsync(pool.id, key.BeatmapId, key.Mods) ?? GlobalBeatmaps[key.BeatmapId];

            PlackettLuce model = new PlackettLuce
            {
                Mu = 1500,
                Sigma = 150,
                Beta = 0,
                Tau = 15.0,
                Gamma = (_, _, _, _, _, _, _) => 1.0
            };

            double clearThreshold = pool.ruleset_id switch
            {
                0 => 550_000,
                1 => 850_000,
                2 => 850_000,
                3 => 850_000,
                _ => throw new ArgumentException("Unknown ruleset ID.")
            };

            IRating[] ratings = model.Rate(
                                         [
                                             new Team { Players = [model.Rating(beatmap.rating, beatmap.rating_sig)] },
                                             .. playerRatings.Select(p => new Team { Players = [model.Rating(p.Mu, p.Sig)] }).ToArray()
                                         ],
                                         scores:
                                         [
                                             clearThreshold,
                                             .. playerScores
                                         ])
                                     .Select(t => t.Players.Single())
                                     .ToArray();

            matchmaking_pool_beatmap newBeatmap = new matchmaking_pool_beatmap(beatmap)
            {
                rating = ratings[0].Mu,
                rating_sig = ratings[0].Sigma
            };

            // Store the beatmap back so that it can be used for subsequent lookups.
            beatmaps[key] = beatmap;

            // Write the beatmap to the database in the next update cycle.
            pendingUpdates.Enqueue(newBeatmap);
        }

        /// <summary>
        /// Retrieves a set of playlist items from the pool within an appropriate difficulty range for the lobby.
        /// </summary>
        /// <param name="count">The number of beatmaps to retrieve.</param>
        /// <param name="ratings">The lobby user ratings.</param>
        public matchmaking_pool_beatmap[] GetAppropriateBeatmaps(int count, EloRating[] ratings)
        {
            if (AppSettings.MatchmakingDebugBeatmaps)
            {
                return Enumerable.Range(1, count).Select(i => new matchmaking_pool_beatmap
                {
                    id = (uint)i,
                    pool_id = pool.id,
                    beatmap_id = 259,
                    checksum = "ea0df9f890e7e9e7ad4d3862a7823359",
                    difficultyrating = 4.18904,
                    rating = 1277
                }).ToArray();
            }

            // Pick from maps around the minimum rating.
            double userRatingMu = ratings.Select(r => r.Mu).DefaultIfEmpty(1500).Min();

            const double rating_sig = 100;

            HashSet<matchmaking_pool_beatmap> maps = [];

            foreach (double rating in randomNumberSamples(count, userRatingMu, rating_sig))
            {
                // Could optimize with binary search?
                var map = beatmaps.Values
                                  .Where(b => !maps.Contains(b))
                                  .MinBy(b => Math.Abs(b.rating - rating));

                if (map == null)
                    break; // happens when more maps are requested than are available

                maps.Add(map);
            }

            return maps.ToArray();
        }

        private static IEnumerable<double> randomNumberSamples(int n, double mu, double sigma)
        {
            return Enumerable.Range(0, (int)Math.Ceiling((double)n / 2))
                             .SelectMany(_ => boxMuller())
                             .Take(n) // Box-Muller returns pairs of numbers, only take as many as we need
                             .Select(x => mu + sigma * x);
        }

        // The Box–Muller transform [..] is a random number sampling method for generating pairs of
        // independent, standard, normally distributed (zero expectation, unit variance) random numbers,
        // given a source of uniformly distributed random numbers.
        // https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform
        private static double[] boxMuller()
        {
            double theta = 2 * Math.PI * Random.Shared.NextDouble();
            double r = Math.Sqrt(-2 * Math.Log(Random.Shared.NextDouble()));
            double x = r * Math.Cos(theta);
            double y = r * Math.Sin(theta);
            return [x, y];
        }

        public readonly record struct BeatmapLookupKey(int BeatmapId, string Mods);
    }
}
