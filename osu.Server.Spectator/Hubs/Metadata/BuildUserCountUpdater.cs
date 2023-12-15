// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            if (!AppSettings.TrackBuildUserCounts)
                return;

            using var db = databaseFactory.GetInstance();

            var mainBuilds = await db.GetAllMainLazerBuildsAsync();
            var platformBuilds = await db.GetAllPlatformSpecificLazerBuildsAsync();

            // note that this is not a one-to-one mapping.
            // a build may be accessible via multiple platform-specific hashes.
            var buildsByHash = constructHashToBuildMapping(mainBuilds, platformBuilds);
            var newUserCounts = mainBuilds.ToDictionary(build => build, _ => (uint)0);

            var usersByHash = clientStates.GetAllEntities()
                                          .Where(kvp => kvp.Value.VersionHash != null)
                                          .GroupBy(kvp => kvp.Value.VersionHash!)
                                          .ToDictionary(grp => grp.Key, grp => (uint)grp.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var (versionHash, userCount) in usersByHash)
            {
                if (buildsByHash.TryGetValue(versionHash, out var build))
                    newUserCounts[build] += userCount;
                else
                    logger.Add($"Unrecognised version hash {versionHash} reported by {userCount} clients. Skipping update.");
            }

            foreach (var (build, newUserCount) in newUserCounts)
            {
                if (build.users != newUserCount)
                {
                    build.users = newUserCount;
                    await db.UpdateBuildUserCountAsync(build);
                }
            }
        }

        private static readonly Regex build_version_regex = new Regex(@"(?<version>\d+\.\d+\.\d+)-lazer-.+", RegexOptions.Compiled);

        private Dictionary<string, osu_build> constructHashToBuildMapping(IEnumerable<osu_build> mainBuilds, IEnumerable<osu_build> platformBuilds)
        {
            var mainBuildsByVersion = mainBuilds.Where(build => build.version != null).ToDictionary(build => build.version!);

            var result = new Dictionary<string, osu_build>(StringComparer.OrdinalIgnoreCase);

            foreach (var platformBuild in platformBuilds)
            {
                if (platformBuild.hash == null)
                {
                    logger.Add($"Data anomaly during creation of hash-to-build mapping: Platform build {platformBuild.build_id} has no hash");
                    continue;
                }

                if (string.IsNullOrEmpty(platformBuild.version))
                {
                    logger.Add($"Data anomaly during creation of hash-to-build mapping: Platform build {platformBuild.build_id} has empty version");
                    continue;
                }

                var match = build_version_regex.Match(platformBuild.version);

                if (!match.Success)
                {
                    logger.Add($"Data anomaly during creation of hash-to-build mapping: Platform build {platformBuild.build_id} has non-conformant version {platformBuild.version}");
                    continue;
                }

                string mainVersion = match.Groups["version"].Value;

                if (!mainBuildsByVersion.TryGetValue(mainVersion, out var mainBuild))
                {
                    logger.Add($"Data anomaly during creation of hash-to-build mapping: No parent build found for platform build {platformBuild.build_id} with version {platformBuild.version}");
                    continue;
                }

                result.Add(string.Concat(platformBuild.hash!.Select(b => b.ToString("X2"))), mainBuild);
            }

            return result;
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }
    }
}
