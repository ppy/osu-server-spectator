// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// Tracks and ensures consistency of a collection of entities that have a related permanent ID.
    /// </summary>
    /// <typeparam name="T">The type of the entity being tracked.</typeparam>
    public class EntityStore<T>
        where T : class
    {
        private readonly Dictionary<long, TrackedEntity> entityMapping = new Dictionary<long, TrackedEntity>();

        private const int lock_timeout = 5000;

        public async Task<ItemUsage<T>> GetForUse(long id)
        {
            TrackedEntity? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                    entityMapping[id] = item = new TrackedEntity(id, this);
            }

            if (!await item.Semaphore.WaitAsync(lock_timeout))
                throw new TimeoutException($"Lock for {nameof(T)} id {id} could not be obtained within timeout period");

            return new ItemUsage<T>(item);
        }

        public async Task Destroy(long id)
        {
            TrackedEntity? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                    // was not tracking.
                    return;
            }

            await item.Semaphore.WaitAsync(lock_timeout);

            // handles removal and disposal of the semaphore.
            item.Destroy();
        }

        private void remove(long id)
        {
            lock (entityMapping)
                entityMapping.Remove(id);
        }

        /// <summary>
        /// Get all tracked entities in an unsafe manner. Only read operations should be performed on retrieved entities.
        /// </summary>
        protected KeyValuePair<long, T?>[] GetAllStates()
        {
            lock (entityMapping)
            {
                return entityMapping
                       .Select(state => new KeyValuePair<long, T?>(state.Key, state.Value.Item))
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

        public class TrackedEntity
        {
            public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

            public T? Item;

            private readonly long id;
            private readonly EntityStore<T> store;

            public TrackedEntity(long id, EntityStore<T> store)
            {
                this.id = id;
                this.store = store;
            }

            /// <summary>
            /// Mark this item as no longer used. Will remove any tracking overhead.
            /// </summary>
            public void Destroy()
            {
                if (Semaphore.CurrentCount > 0)
                    throw new InvalidOperationException("Attempted to destroy a tracked entity without holding a lock");

                store.remove(id);
                Semaphore.Dispose();
            }
        }
    }
}
