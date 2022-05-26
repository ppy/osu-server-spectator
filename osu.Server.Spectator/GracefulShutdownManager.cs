// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator;

/// <summary>
/// Ensures that shutdown is delayed until any existing usages have ceased.
/// </summary>
public class GracefulShutdownManager
{
    private readonly List<IEntityStore> dependentStores = new List<IEntityStore>();

    private Task? shutdownTask;

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
    public void AddDependentStore(IEntityStore? store)
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

            int timeWaited = 0;

            const int seconds_between_checks = 10;
            const int max_hours_before_forceful_shutdown = 6;

            while (timeWaited < 3600 * max_hours_before_forceful_shutdown)
            {
                var remaining = dependentStores.Select(store => (store.EntityName, store.RemainingUsages));

                if (remaining.Sum(s => s.RemainingUsages) == 0)
                    break;

                Logger.Log("Waiting for usages of existing entities to finish...");
                foreach (var r in remaining)
                    Logger.Log($"{r.EntityName,10}: {r.RemainingUsages}");

                Thread.Sleep(seconds_between_checks * 1000);
                timeWaited += seconds_between_checks;
            }

            Logger.Log("All entities cleaned up. Server shutdown unblocking.");
        }
    }
}
