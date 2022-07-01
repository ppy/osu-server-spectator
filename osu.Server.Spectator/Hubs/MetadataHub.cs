// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class MetadataHub : Hub<IMetadataClient>, IMetadataServer
    {
        private readonly IDatabaseFactory databaseFactory;

        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private uint? lastQueueId;

        public MetadataHub(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            Task.Factory.StartNew(pollForChanges, cts.Token);
        }

        public async Task<(int[], uint)> GetChangesSince(uint queueId)
        {
            using (var db = databaseFactory.GetInstance())
                return (await db.GetUpdatedBeatmapSets(lastQueueId));
        }

        private async Task pollForChanges()
        {
            while (!cts.IsCancellationRequested)
            {
                using (var db = databaseFactory.GetInstance())
                {
                    (int[] beatmapSetIds, lastQueueId) = await db.GetUpdatedBeatmapSets(lastQueueId);

                    await Clients.All.BeatmapSetsUpdated(beatmapSetIds);
                }

                await Task.Delay(1000, cts.Token);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            cts.Cancel();
        }
    }

    public interface IMetadataClient
    {
        Task BeatmapSetsUpdated(IEnumerable<int> beatmapSetIds);
    }

    /// <summary>An interface defining the spectator server instance.</summary>
    public interface IMetadataServer
    {
        /// <summary>
        /// Get any changes since a specific point in the queue.
        /// Should be used to allow the client to catch up with any changes after being closed or disconnected.
        /// </summary>
        /// <param name="queueId">The last processed queue ID.</param>
        /// <returns></returns>
        Task<(int[], uint)> GetChangesSince(uint queueId);
    }
}
