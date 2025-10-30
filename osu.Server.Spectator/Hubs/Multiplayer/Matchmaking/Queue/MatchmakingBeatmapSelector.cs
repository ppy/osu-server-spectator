// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
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
                b.rating ??= (int)Math.Round(800 + 150 * b.difficultyrating);

            return new MatchmakingBeatmapSelector(beatmaps);
        }

        /// <summary>
        /// Retrieves a set of playlist items from the pool within an appropriate difficulty range for the lobby.
        /// </summary>
        /// <param name="ratings">The lobby user ratings.</param>
        public matchmaking_pool_beatmap[] GetAppropriateBeatmaps(EloRating[] ratings)
        {
            double muMin = ratings.Min(r => r.Mu);
            double muMax = ratings.Max(r => r.Mu);
            double muAvg = ratings.Sum(r => r.Mu) / ratings.Length;
            // 80 is a sane value that ensures the window expands. It is equal to EloSystem.SigLimit.
            double sigAvg = Math.Max(80, Math.Sqrt(ratings.Sum(r => Math.Pow(r.Sig, 2))) / ratings.Length);

            List<matchmaking_pool_beatmap> result = [];

            // 25% of beatmaps will be easy for the lobby.
            result.AddRange(collectBeatmaps(beatmaps.Where(b => b.rating <= muMin), PoolSize / 4, muAvg, sigAvg));

            // 25% of beatmaps will be hard for the lobby.
            result.AddRange(collectBeatmaps(beatmaps.Where(b => b.rating >= muMax).Except(result), PoolSize / 4, muAvg, sigAvg));

            // The rest will be of average difficulty.
            result.AddRange(collectBeatmaps(beatmaps.Except(result), PoolSize - result.Count, muAvg, sigAvg));

            return result.ToArray();
        }

        /// <summary>
        /// Gradually selects a set of beatmaps with an expanding rating window.
        /// </summary>
        /// <param name="beatmaps">The available beatmaps.</param>
        /// <param name="countToAdd">The number of beatmaps to be added.</param>
        /// <param name="ratingMu">The window rating mean.</param>
        /// <param name="ratingSig">The window rating variance.</param>
        private static matchmaking_pool_beatmap[] collectBeatmaps(IEnumerable<matchmaking_pool_beatmap> beatmaps, int countToAdd, double ratingMu, double ratingSig)
        {
            // Algorithm:
            // - Start at the given rating and gradually expand a window outwards.
            // - Each iteration adds a set of beatmaps to the resultant set.
            // - The process repeats with the window size and number of items added increasing,
            //   until the required number of beatmaps have been added.
            //
            // Start with 3 standard deviations, adding a maximum of 1 item for each one.
            //
            // For example, with a variance of 100:
            //   Iteration 1: adds 1 item within each of ±100, ±200, and ±300 rating.
            //   Iteration 2: adds 2 items within each of ±100, ±200, ±300, and ±400 rating.
            //
            // This biases the difficulty of items added to be closer to the mean than the outer extremes while still allowing some variation.

            HashSet<matchmaking_pool_beatmap> available = [..beatmaps];
            List<matchmaking_pool_beatmap> result = [];

            int windowSize = 3;
            int itemsToAdd = 1;

            while (available.Count > 0 && result.Count < countToAdd)
            {
                for (int iteration = 0; iteration < windowSize; iteration++)
                {
                    double windowSig = ratingSig * (iteration + 1);

                    matchmaking_pool_beatmap[] windowItems = available.Where(b => Math.Abs((b.rating ?? 1500) - ratingMu) <= windowSig).ToArray();
                    Random.Shared.Shuffle(windowItems);

                    foreach (matchmaking_pool_beatmap item in windowItems.Take(itemsToAdd))
                    {
                        result.Add(item);
                        available.Remove(item);
                    }
                }

                // Next iteration considers slightly more beatmaps.
                itemsToAdd++;
                windowSize++;
            }

            return result.Take(countToAdd).ToArray();
        }
    }
}
