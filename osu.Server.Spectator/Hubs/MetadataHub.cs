// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
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

        public async Task<BeatmapUpdates> GetChangesSince(uint queueId)
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
                    var updates = await db.GetUpdatedBeatmapSets(lastQueueId);

                    lastQueueId = updates.LastProcessedQueueID;

                    if (updates.BeatmapSetIDs.Any())
                        await Clients.All.BeatmapSetsUpdated(updates);
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

    /// <summary>
    /// Describes a set of beatmaps which have been updated in some way.
    /// </summary>
    [MessagePackObject(false)]
    [Serializable]
    public class BeatmapUpdates
    {
        [Key(0)]
        public int[] BeatmapSetIDs { get; set; }

        [Key(1)]
        public uint LastProcessedQueueID { get; set; }

        public BeatmapUpdates(int[] beatmapSetIDs, uint lastProcessedQueueID)
        {
            BeatmapSetIDs = beatmapSetIDs;
            LastProcessedQueueID = lastProcessedQueueID;
        }
    }

    public interface IMetadataClient
    {
        Task BeatmapSetsUpdated(BeatmapUpdates updates);
    }

    /// <summary>
    /// Metadata server is responsible for keeping the osu! client up-to-date with any changes.
    /// </summary>
    public interface IMetadataServer
    {
        /// <summary>
        /// Get any changes since a specific point in the queue.
        /// Should be used to allow the client to catch up with any changes after being closed or disconnected.
        /// </summary>
        /// <param name="queueId">The last processed queue ID.</param>
        /// <returns></returns>
        Task<BeatmapUpdates> GetChangesSince(uint queueId);
    }
}
