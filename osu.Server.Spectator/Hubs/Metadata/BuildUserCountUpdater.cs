// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger logger;

        private CancellationTokenSource? cancellationSource;

        public BuildUserCountUpdater(
            EntityStore<MetadataClientState> clientStates,
            IDatabaseFactory databaseFactory,
            ILoggerFactory loggerFactory)
        {
            this.clientStates = clientStates;
            this.databaseFactory = databaseFactory;

            cancellationSource = new CancellationTokenSource();
            logger = loggerFactory.CreateLogger(nameof(BuildUserCountUpdater));

            Task.Factory.StartNew(runUpdateLoop, TaskCreationOptions.LongRunning);
        }

        private void runUpdateLoop()
        {
            while (cancellationSource?.IsCancellationRequested == false)
            {
                try
                {
                    updateBuildUserCounts().Wait();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update build user counts");
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
                    logger.LogInformation("Unrecognised version hash {versionHash} reported by {userCount} clients. Skipping update.", versionHash, userCount);
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

        // Should match checks in DatabaseAccess.GetAllPlatformSpecificLazerBuildsAsync.
        private static readonly Regex build_version_regex = new Regex(@"(?<version>\d+\.\d+\.\d+)-(?:lazer|tachyon)-.+", RegexOptions.Compiled);

        private Dictionary<string, osu_build> constructHashToBuildMapping(IEnumerable<osu_build> mainBuilds, IEnumerable<osu_build> platformBuilds)
        {
            var mainBuildsByVersion = mainBuilds.Where(build => build.version != null).ToDictionary(build => build.version!);

            var result = new Dictionary<string, osu_build>(StringComparer.OrdinalIgnoreCase);

            foreach (var platformBuild in platformBuilds)
            {
                if (platformBuild.hash == null)
                {
                    logger.LogInformation("Data anomaly during creation of hash-to-build mapping: Platform build {buildId} has no hash", platformBuild.build_id);
                    continue;
                }

                if (string.IsNullOrEmpty(platformBuild.version))
                {
                    logger.LogInformation("Data anomaly during creation of hash-to-build mapping: Platform build {buildId} has empty version", platformBuild.build_id);
                    continue;
                }

                var match = build_version_regex.Match(platformBuild.version);

                if (!match.Success)
                {
                    logger.LogInformation("Data anomaly during creation of hash-to-build mapping: Platform build {buildId} has non-conformant version {version}",
                        platformBuild.build_id,
                        platformBuild.version);
                    continue;
                }

                string mainVersion = match.Groups["version"].Value;

                if (!mainBuildsByVersion.TryGetValue(mainVersion, out var mainBuild))
                {
                    logger.LogInformation("Data anomaly during creation of hash-to-build mapping: No parent build found for platform build {buildId} with version {version}",
                        platformBuild.build_id,
                        platformBuild.version);
                    continue;
                }

                result.Add(string.Concat(platformBuild.hash!.Select(b => b.ToString("X2"))), mainBuild);
            }

            return result;
        }

        public void Dispose()
        {
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            cancellationSource = null;
        }
    }
}
