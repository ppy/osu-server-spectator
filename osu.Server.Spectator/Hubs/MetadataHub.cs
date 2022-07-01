// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using MessagePack;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class MetadataBroadcaster : IDisposable
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MetadataHub> metadataHubContext;

        private readonly Timer timer;

        private uint? lastQueueId;

        public MetadataBroadcaster(IDatabaseFactory databaseFactory, IHubContext<MetadataHub> metadataHubContext)
        {
            this.databaseFactory = databaseFactory;
            this.metadataHubContext = metadataHubContext;

            timer = new Timer(5000);
            timer.AutoReset = false;
            timer.Elapsed += pollForChanges;
            timer.Start();
        }

        private async void pollForChanges(object? sender, ElapsedEventArgs args)
        {
            try
            {
                using (var db = databaseFactory.GetInstance())
                {
                    var updates = await db.GetUpdatedBeatmapSets(lastQueueId);

                    lastQueueId = updates.LastProcessedQueueID;
                    Console.WriteLine($"Polled beatmap changes up to last queue id {updates.LastProcessedQueueID}");

                    if (updates.BeatmapSetIDs.Any())
                    {
                        Console.WriteLine($"Broadcasting new beatmaps to client: {string.Join(',', updates.BeatmapSetIDs.Select(i => i.ToString()))}");
                        await metadataHubContext.Clients.All.SendAsync(nameof(IMetadataClient.BeatmapSetsUpdated), updates);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during beatmap update polling: {e}");
            }

            timer.Start();
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }
    }

    public class MetadataHub : Hub<IMetadataClient>, IMetadataServer
    {
        private readonly IDatabaseFactory databaseFactory;

        public MetadataHub(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;
        }

        public async Task<BeatmapUpdates> GetChangesSince(uint queueId)
        {
            using (var db = databaseFactory.GetInstance())
                return await db.GetUpdatedBeatmapSets(queueId);
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
