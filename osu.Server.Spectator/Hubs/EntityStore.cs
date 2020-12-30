// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    public interface IEntityStore
    {
    }

    /// <summary>
    /// Tracks and ensures consistency of a collection of entities that have a related permanent ID.
    /// </summary>
    /// <typeparam name="T">The type of the entity being tracked.</typeparam>
    /// <typeparam name="TKey">The numeric type of the key (generally int or long).</typeparam>
    public class EntityStore<T, TKey>
        where T : class
        where TKey : struct
    {
        private readonly Dictionary<TKey, TrackedEntity<T>> entityMapping = new Dictionary<TKey, TrackedEntity<T>>();

        private const int lock_timeout = 5000;

        public async Task<ItemUsage<T>> GetForUse(TKey id)
        {
            TrackedEntity<T>? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                    entityMapping[id] = item = new TrackedEntity<T>();
            }

            if (!await item.Semaphore.WaitAsync(lock_timeout))
                throw new TimeoutException($"Lock for {nameof(T)} id {id} could not be obtained within timeout period");

            return new ItemUsage<T>(item);
        }

        public async Task Destroy(TKey id)
        {
            TrackedEntity<T>? item;

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
        protected KeyValuePair<TKey, T?>[] GetAllStates()
        {
            lock (entityMapping)
            {
                return entityMapping
                       .Select(state => new KeyValuePair<TKey, T?>(state.Key, state.Value.Item))
                       .ToArray();
            }
        }

        /// <summary>
        /// Clear all tracked entities.
        /// </summary>
        public void Clear()
        {
            lock (entityMapping)
            {
                entityMapping.Clear();
            }
        }
    }
}
