// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Hosting;
using osu.Framework.Logging;
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

    public GracefulShutdownManager(EntityStore<ServerMultiplayerRoom> roomStore, EntityStore<SpectatorClientState> clientStateStore, IHostApplicationLifetime hostApplicationLifetime)
    {
        addDependentStore(roomStore);
        addDependentStore(clientStateStore);

        hostApplicationLifetime.ApplicationStopping.Register(shutdownSafely);
    }

    /// <summary>
    /// Add an entity store which should be waited on for all usages to have finished.
    /// </summary>
    /// <param name="store"></param>
    private void addDependentStore(IEntityStore? store)
    {
        if (store == null)
            return;

        dependentStores.Add(store);
    }

    private void shutdownSafely()
    {
        Logger.Log("Server shutdown triggered");

        foreach (var store in dependentStores)
            store.StopAcceptingEntities();

        TimeSpan timeWaited = new TimeSpan();

        TimeSpan timeBetweenChecks = TimeSpan.FromSeconds(10);

        while (timeWaited < TIME_BEFORE_FORCEFUL_SHUTDOWN)
        {
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
}
