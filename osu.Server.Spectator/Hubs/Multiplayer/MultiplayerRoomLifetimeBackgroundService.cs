// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Referee;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerRoomLifetimeBackgroundService : BackgroundService
    {
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<RefereeClientState> referees;
        private readonly ILogger<MultiplayerRoomLifetimeBackgroundService> logger;

        public MultiplayerRoomLifetimeBackgroundService(
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<RefereeClientState> referees,
            ILoggerFactory loggerFactory)
        {
            this.rooms = rooms;
            this.referees = referees;
            logger = loggerFactory.CreateLogger<MultiplayerRoomLifetimeBackgroundService>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Running clean-up of rooms with set end dates.");

                long[] endedRoomIds = rooms.GetAllEntities()
                                           .Where(kv => kv.Value.EndDate != null && DateTimeOffset.Now > kv.Value.EndDate)
                                           .Select(kv => kv.Key)
                                           .ToArray();
                var actualCleanedUpRoomIds = new HashSet<long>();

                foreach (long roomId in endedRoomIds)
                {
                    using (var roomUsage = await rooms.TryGetForUse(roomId))
                    {
                        // since `GetAllEntities()` above, someone else could have touched the room and caused the end date to change again.
                        // double-check inside the lock to be safe.
                        if (roomUsage?.Item?.EndDate == null || DateTimeOffset.Now <= roomUsage.Item.EndDate)
                        {
                            logger.LogDebug("Skipping attempt to destroy usage of room ID:{RoomId} due as its end date has changed to {EndDate}", roomId, roomUsage?.Item?.EndDate);
                            continue;
                        }

                        await roomUsage.Item.Disband(disbandingUserId: null);

                        logger.LogDebug("Destroying usage of room ID:{RoomId} as its end date of {EndDate} has passed", roomId, roomUsage.Item.EndDate);
                        roomUsage.Destroy();
                        actualCleanedUpRoomIds.Add(roomId);
                    }
                }

                long[] allRefereeIds = referees.GetAllEntities().Select(kv => kv.Key).ToArray();

                foreach (long refereeId in allRefereeIds)
                {
                    using (var refereeUsage = await referees.TryGetForUse(refereeId))
                    {
                        if (refereeUsage?.Item is RefereeClientState refereeClientState)
                            refereeClientState.DisassociateFromRooms(actualCleanedUpRoomIds);
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
