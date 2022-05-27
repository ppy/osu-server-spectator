// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public static readonly TimeSpan TIME_BEFORE_FORCEFUL_SHUTDOWN = TimeSpan.FromHours(6);

    private readonly List<IEntityStore> dependentStores = new List<IEntityStore>();

    private Task? shutdownTask;

    public GracefulShutdownManager(EntityStore<ServerMultiplayerRoom> roomStore, EntityStore<SpectatorClientState> clientStateStore, IHostApplicationLifetime hostApplicationLifetime)
    {
        addDependentStore(roomStore);
        addDependentStore(clientStateStore);

        hostApplicationLifetime.ApplicationStopping.Register(WaitForSafeShutdown);
    }

    /// <summary>
    /// Blocks until safe to continue with shutdown. Can be invoked from multiple locations.
    /// </summary>
    public void WaitForSafeShutdown()
    {
        lock (this)
            shutdownTask ??= Task.Factory.StartNew(shutdownSafely, TaskCreationOptions.LongRunning);

        shutdownTask.Wait();
    }

    /// <summary>
    /// Add an entity store which should be waited on for all usages to have finished.
    /// </summary>
    /// <param name="store"></param>
    private void addDependentStore(IEntityStore? store)
    {
        if (store == null)
            return;

        lock (dependentStores)
            dependentStores.Add(store);
    }

    private void shutdownSafely()
    {
        lock (dependentStores)
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
}
