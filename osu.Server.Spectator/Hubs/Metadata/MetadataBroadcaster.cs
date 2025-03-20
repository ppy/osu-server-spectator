// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Metadata;
using osu.Server.QueueProcessor;
using osu.Server.Spectator.Database;
using BeatmapUpdates = osu.Server.QueueProcessor.BeatmapUpdates;

namespace osu.Server.Spectator.Hubs.Metadata
{
    /// <summary>
    /// A service which broadcasts any new metadata changes to <see cref="MetadataHub"/>.
    /// </summary>
    public class MetadataBroadcaster : IDisposable
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MetadataHub> metadataHubContext;

        private readonly ILogger logger;

        private readonly IDisposable poller;

        public MetadataBroadcaster(
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            IHubContext<MetadataHub> metadataHubContext)
        {
            this.databaseFactory = databaseFactory;
            this.metadataHubContext = metadataHubContext;

            logger = loggerFactory.CreateLogger(nameof(MetadataBroadcaster));
            poller = BeatmapStatusWatcher.StartPollingAsync(handleUpdates, 5000).Result;
        }

        // ReSharper disable once AsyncVoidMethod
        private async void handleUpdates(BeatmapUpdates updates)
        {
            logger.LogInformation("Polled beatmap changes up to last queue id {lastProcessedQueueID}", updates.LastProcessedQueueID);

            if (updates.BeatmapSetIDs.Any())
            {
                logger.LogInformation("Broadcasting new beatmaps to client: {beatmapIds}", string.Join(',', updates.BeatmapSetIDs.Select(i => i.ToString())));
                await metadataHubContext.Clients.All.SendAsync(nameof(IMetadataClient.BeatmapSetsUpdated), updates);
            }
        }

        public void Dispose()
        {
            poller.Dispose();
        }
    }
}
