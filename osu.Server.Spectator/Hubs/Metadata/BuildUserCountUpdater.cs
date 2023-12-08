// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class BuildUserCountUpdater : IDisposable
    {
        /// <summary>
        /// Amount of time (in milliseconds) between subsequent updates of user counts.
        /// </summary>
        public int UpdateInterval = 300_000;

        private readonly EntityStore<MetadataClientState> clientStates;
        private readonly IDatabaseFactory databaseFactory;
        private readonly CancellationTokenSource cancellationSource;
        private readonly Logger logger;

        public BuildUserCountUpdater(
            EntityStore<MetadataClientState> clientStates,
            IDatabaseFactory databaseFactory)
        {
            this.clientStates = clientStates;
            this.databaseFactory = databaseFactory;

            cancellationSource = new CancellationTokenSource();
            logger = Logger.GetLogger(nameof(BuildUserCountUpdater));

            Task.Factory.StartNew(runUpdateLoop, TaskCreationOptions.LongRunning);
        }

        private void runUpdateLoop()
        {
            while (!cancellationSource.IsCancellationRequested)
            {
                try
                {
                    updateBuildUserCounts().Wait();
                }
                catch (Exception ex)
                {
                    logger.Add("Failed to update build user counts", LogLevel.Error, ex);
                }

                Thread.Sleep(UpdateInterval);
            }
        }

        private async Task updateBuildUserCounts()
        {
            using var db = databaseFactory.GetInstance();

            IEnumerable<osu_build> builds = await db.GetAllLazerBuildsAsync();
            Dictionary<string, osu_build> buildsByHash = builds.Where(build => build.hash != null)
                                                               .ToDictionary(build => string.Concat(build.hash!.Select(b => b.ToString("X2"))), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, int> usersByHash = clientStates.GetAllEntities()
                                                              .Where(kvp => kvp.Value.VersionHash != null)
                                                              .GroupBy(kvp => kvp.Value.VersionHash!)
                                                              .ToDictionary(grp => grp.Key, grp => grp.Count());

            foreach (var (hash, count) in usersByHash)
            {
                if (buildsByHash.TryGetValue(hash, out var build))
                {
                    build.users = (uint)count;
                    await db.UpdateBuildUserCountAsync(build);
                }
                else
                {
                    logger.Add($"Unrecognised version hash {hash} reported by {count} clients. Skipping update.");
                }
            }
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }
    }
}
