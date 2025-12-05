// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingBeatmapSelector
    {
        public int PoolSize { get; set; } = AppSettings.MatchmakingPoolSize;

        private readonly matchmaking_pool_beatmap[] beatmaps;

        public MatchmakingBeatmapSelector(matchmaking_pool_beatmap[] beatmaps)
        {
            this.beatmaps = beatmaps;
        }

        /// <summary>
        /// Creates a new <see cref="MatchmakingBeatmapSelector"/>.
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="dbFactory">The database factory.</param>
        public static async Task<MatchmakingBeatmapSelector> Initialise(matchmaking_pool pool, IDatabaseFactory dbFactory)
        {
            using var db = dbFactory.GetInstance();

            matchmaking_pool_beatmap[] beatmaps = await db.GetMatchmakingPoolBeatmapsAsync(pool.id);

            foreach (var b in beatmaps)
            {
                // Todo: This default rating is only accurate for NoMod beatmaps.
                b.rating ??= (int)Math.Round(800 + 150 * b.difficultyrating);
            }

            return new MatchmakingBeatmapSelector(beatmaps);
        }

        /// <summary>
        /// Retrieves a set of playlist items from the pool within an appropriate difficulty range for the lobby.
        /// </summary>
        /// <param name="ratings">The lobby user ratings.</param>
        public matchmaking_pool_beatmap[] GetAppropriateBeatmaps(EloRating[] ratings)
        {
            // Pick from maps around the minimum rating.
            double ratingMu = ratings.Select(r => r.Mu).DefaultIfEmpty(1500).Min();
            // Constant standard deviation to give a wide breadth around mu.
            const double rating_sig = 200;

            return beatmaps.OrderByDescending(b =>
                           {
                               double beatmapRating = b.rating ?? 1500;
                               // The clamp attempts to ensure all beatmaps are given some chance of being selected.
                               double weight = Math.Clamp(Math.Exp(-Math.Pow(beatmapRating - ratingMu, 2) / (2 * rating_sig * rating_sig)), 0.1, 1);
                               return Math.Pow(Random.Shared.NextDouble(), 1.0 / weight);
                           })
                           .Take(PoolSize)
                           .ToArray();
        }
    }
}
