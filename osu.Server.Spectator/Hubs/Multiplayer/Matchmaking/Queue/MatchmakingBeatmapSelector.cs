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

            foreach (var b in beatmaps)
            {
                // Todo: This default rating is only accurate for NoMod beatmaps.
                b.rating ??= (int)Math.Round(800 + 150 * b.difficultyrating);
            }
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
                matchmaking_pool_beatmap[] beatmaps = await db.GetMatchmakingPoolBeatmapsAsync(pool.id);

                // If there are no beatmaps specified in the pool, use all global ranked beatmaps.
                if (beatmaps.Length == 0)
                {
                    database_beatmap[] globalBeatmaps = await db.GetMatchmakingGlobalPoolBeatmapsAsync(pool.ruleset_id, pool.variant_id);
                    beatmaps = globalBeatmaps.Select(b => new matchmaking_pool_beatmap
                    {
                        beatmap_id = b.beatmap_id,
                        checksum = b.checksum,
                        difficultyrating = b.difficultyrating
                    }).ToArray();
                }

                return new MatchmakingBeatmapSelector(beatmaps);
            }
        }

        /// <summary>
        /// Retrieves a set of playlist items from the pool within an appropriate difficulty range for the lobby.
        /// </summary>
        /// <param name="ratings">The lobby user ratings.</param>
        public matchmaking_pool_beatmap[] GetAppropriateBeatmaps(EloRating[] ratings)
        {
            return ratings.SelectMany(r =>
                          {
                              // 80 is a safe value that is the lower limit of EloSystem.
                              double ratingSig = Math.Max(80, r.Sig);
                              return beatmaps.OrderByDescending(b =>
                              {
                                  double beatmapRating = b.rating ?? 1500;
                                  // The clamp attempts to ensure all beatmaps are given some chance of being selected.
                                  double weight = Math.Clamp(Math.Exp(-Math.Pow(beatmapRating - r.Mu, 2) / (2 * ratingSig * ratingSig)), 0.1, 1);
                                  return Math.Pow(Random.Shared.NextDouble(), 1.0 / weight);
                              }).Take(PoolSize);
                          })
                          .OrderBy(_ => Random.Shared.NextDouble())
                          .Distinct()
                          .Take(PoolSize)
                          .ToArray();
        }
    }
}
