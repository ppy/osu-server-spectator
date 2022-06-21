// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class MetadataHub : Hub<IBeatmapClient>
    {
        private readonly IDatabaseFactory databaseFactory;

        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private uint? lastQueueId;

        public MetadataHub(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            Task.Factory.StartNew(pollForChanges, cts.Token);
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

                Thread.Sleep(1000);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            cts.Cancel();
        }
    }

    public interface IBeatmapClient
    {
        Task BeatmapSetsUpdated(IEnumerable<int> beatmapSetIds);
    }
}
