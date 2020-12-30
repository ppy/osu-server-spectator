// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// Tracks and ensures consistency of a collection of entities that have a related permanent ID.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class EntityStore<T>
        where T : class
    {
        private readonly Dictionary<int, Entity<T>> entityMapping = new Dictionary<int, Entity<T>>();

        private const int lock_timeout = 5000;

        public async Task<ItemUsage<T>> GetForUse(int id)
        {
            Entity<T>? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                    entityMapping[id] = item = new Entity<T>();
            }

            if (!await item.Semaphore.WaitAsync(lock_timeout))
                throw new TimeoutException($"Lock for {nameof(T)} id {id} could not be obtained within timeout period");

            return new ItemUsage<T>(item);
        }

        public async Task Destroy(int id)
        {
            Entity<T>? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                    // was not tracking.
                    return;
            }

            await item.Semaphore.WaitAsync(lock_timeout);

            lock (entityMapping)
                entityMapping.Remove(id);

            item.Semaphore.Dispose();
        }

        /// <summary>
        /// Get all tracked entities in an unsafe manner. Only read operations should be performed on retrieved entities.
        /// </summary>
        protected KeyValuePair<int, T?>[] GetAllStates()
        {
            lock (entityMapping)
            {
                return entityMapping
                       .Select(state => new KeyValuePair<int, T?>(state.Key, state.Value.Item))
                       .ToArray();
            }
        }
    }
}
