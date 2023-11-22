// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Metadata
{
    /// <summary>
    /// Performes updates of user metadata in a batched manner.
    /// </summary>
    public class BatchMetadataUpdater : IDisposable
    {
        /// <summary>
        /// Amount of time (in milliseconds) between update batches.
        /// Also used to stagger updates in a single batch.
        /// </summary>
        public int BatchInterval { get; set; } = 300_000;

        /// <summary>
        /// Maximum number of rows to update at once.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        private readonly IDatabaseFactory databaseFactory;
        private readonly EntityStore<MetadataClientState> clientStates;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly Logger logger;

        public BatchMetadataUpdater(
            IDatabaseFactory databaseFactory,
            EntityStore<MetadataClientState> clientStates)
        {
            this.databaseFactory = databaseFactory;
            this.clientStates = clientStates;

            logger = Logger.GetLogger(nameof(BatchMetadataUpdater));
            cancellationTokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(updateLastVisitTimes, TaskCreationOptions.LongRunning);
        }

        private void updateLastVisitTimes()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var clientStatesSnapshot = clientStates.GetAllEntities();

                    var batches = clientStatesSnapshot.Select((kvp, index) => (kvp.Value.UserId, index))
                                                      .GroupBy(pair => pair.index / BatchSize, pair => pair.UserId)
                                                      .ToList();

                    if (!batches.Any())
                        continue;

                    int updateInterval = (int)Math.Round((double)BatchInterval / batches.Count);

                    using (var connection = databaseFactory.GetInstance())
                    {
                        foreach (var batch in batches)
                            connection.UpdateLastVisitTimeToNowAsync(batch);

                        Thread.Sleep(updateInterval);
                    }
                }
                catch (Exception ex)
                {
                    logger.Add($"Failed to update users' last visit times", LogLevel.Error, ex);
                }
                finally
                {
                    Thread.Sleep(BatchInterval);
                }
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
    }
}
