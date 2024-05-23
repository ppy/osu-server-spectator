// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Metadata;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public interface IDailyChallengeUpdater : IHostedService
    {
        DailyChallengeInfo? Current { get; }
    }

    public class DailyChallengeUpdater : BackgroundService, IDailyChallengeUpdater
    {
        /// <summary>
        /// Amount of time (in milliseconds) between subsequent polls for the current beatmap of the day.
        /// </summary>
        public int UpdateInterval = 60_000;

        public DailyChallengeInfo? Current { get; private set; }

        private readonly ILogger logger;
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MetadataHub> hubContext;

        public DailyChallengeUpdater(
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            IHubContext<MetadataHub> hubContext)
        {
            logger = loggerFactory.CreateLogger(nameof(DailyChallengeUpdater));
            this.databaseFactory = databaseFactory;
            this.hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await updateDailyChallengeInfo(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update beatmap of the day");
                }

                await Task.Delay(UpdateInterval, stoppingToken);
            }
        }

        private async Task updateDailyChallengeInfo(CancellationToken cancellationToken)
        {
            using var db = databaseFactory.GetInstance();

            var activeRooms = (await db.GetActiveDailyChallengeRoomsAsync()).ToList();

            if (activeRooms.Count > 1)
            {
                logger.LogWarning("More than one active 'beatmap of the day' room detected (ids: {roomIds}). Will only use the first one.",
                    string.Join(',', activeRooms.Select(room => room.id)));
            }

            DailyChallengeInfo? newInfo = null;

            var activeRoom = activeRooms.FirstOrDefault();

            if (activeRoom?.id != null)
                newInfo = new DailyChallengeInfo { RoomID = activeRoom.id };

            if (!Current.Equals(newInfo))
            {
                logger.LogInformation("Broadcasting 'beatmap of the day' room change from id {oldRoomID} to {newRoomId}", Current?.RoomID, newInfo?.RoomID);
                Current = newInfo;
                await hubContext.Clients.All.SendAsync(nameof(IMetadataClient.DailyChallengeUpdated), Current, cancellationToken);
            }
        }
    }
}
