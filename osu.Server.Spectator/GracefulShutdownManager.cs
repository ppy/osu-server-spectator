// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using osu.Framework.Logging;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator;

/// <summary>
/// Ensures that shutdown is delayed until any existing usages have ceased.
/// </summary>
public class GracefulShutdownManager
{
    // This should probably be configurable in the future.
    // 6 hours is way too long, but set initially to test the whole process out.
    // We can manually override this for immediate shutdown if/when required from a kubernetes or docker level.
    public static readonly TimeSpan TIME_BEFORE_FORCEFUL_SHUTDOWN = TimeSpan.FromHours(6);

    private readonly List<IEntityStore> dependentStores = new List<IEntityStore>();
    private readonly EntityStore<ServerMultiplayerRoom> roomStore;

    public GracefulShutdownManager(EntityStore<ServerMultiplayerRoom> roomStore, EntityStore<SpectatorClientState> clientStateStore, IHostApplicationLifetime hostApplicationLifetime)
    {
        this.roomStore = roomStore;

        dependentStores.Add(roomStore);
        dependentStores.Add(clientStateStore);

        hostApplicationLifetime.ApplicationStopping.Register(shutdownSafely);
    }

    private void shutdownSafely()
    {
        Logger.Log("Server shutdown triggered");

        foreach (var store in dependentStores)
            store.StopAcceptingEntities();

        performOnAllRooms(async r =>
        {
            await r.StartCountdown(new ServerShuttingDownCountdown
            {
                TimeRemaining = TIME_BEFORE_FORCEFUL_SHUTDOWN
            });
        }).Wait();

        TimeSpan timeWaited = new TimeSpan();
        TimeSpan timeBetweenChecks = TimeSpan.FromSeconds(10);
        bool finalNotificationSent = false;

        while (timeWaited < TIME_BEFORE_FORCEFUL_SHUTDOWN)
        {
            TimeSpan timeRemaining = TIME_BEFORE_FORCEFUL_SHUTDOWN - timeWaited;

            if (timeRemaining.TotalMinutes <= 5 && !finalNotificationSent)
            {
                performOnAllRooms(async r =>
                {
                    await r.StartCountdown(new ServerShuttingDownCountdown
                    {
                        TimeRemaining = timeRemaining,
                        FinalNotification = true
                    });
                }).Wait();

                finalNotificationSent = true;
            }

            var remaining = dependentStores.Select(store => (store.EntityName, store.RemainingUsages));

            if (remaining.Sum(s => s.RemainingUsages) == 0)
                break;

            Logger.Log("Waiting for usages of existing entities to finish...");
            foreach (var r in remaining)
                Logger.Log($"{r.EntityName,10}: {r.RemainingUsages}");

            Thread.Sleep(timeBetweenChecks);
            timeWaited = timeWaited.Add(timeBetweenChecks);
        }

        Logger.Log("All entities cleaned up. Server shutdown unblocking.");
    }

    private async Task performOnAllRooms(Func<ServerMultiplayerRoom, Task> action)
    {
        var rooms = roomStore.GetAllEntities();

        foreach (var roomId in rooms.Select(r => r.Key))
        {
            using (ItemUsage<ServerMultiplayerRoom> roomUsage = await roomStore.GetForUse(roomId))
            {
                if (roomUsage.Item != null)
                    await action(roomUsage.Item);
            }
        }
    }
}
