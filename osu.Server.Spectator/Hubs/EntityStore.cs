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

        public async Task<ItemUsage<T>> GetForUse(long id, bool createOnMissing = false)
        {
            TrackedEntity? item;

            lock (entityMapping)
            {
                if (!entityMapping.TryGetValue(id, out item))
                {
                    if (!createOnMissing)
                        throw new ArgumentException($"Attempted to get untracked entity {nameof(T)} id {id}", nameof(id));

                    entityMapping[id] = item = new TrackedEntity(id, this);
                }
            }

            await item.ObtainLockAsync();

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

            await item.ObtainLockAsync();

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
            private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

            private T? item;

            private readonly long id;
            private readonly EntityStore<T> store;

            private bool isDestroyed;

            private bool isLocked => semaphore.CurrentCount == 0;

            public TrackedEntity(long id, EntityStore<T> store)
            {
                this.id = id;
                this.store = store;
            }

            public T? Item
            {
                get
                {
                    checkValidForUse();
                    return item;
                }
                set
                {
                    checkValidForUse();
                    item = value;
                }
            }

            /// <summary>
            /// Mark this item as no longer used. Will remove any tracking overhead.
            /// </summary>
            public void Destroy()
            {
                // we should already have a lock when calling destroy.
                checkValidForUse();

                store.remove(id);
                semaphore.Dispose();
                isDestroyed = true;
            }

            public async Task ObtainLockAsync()
            {
                checkValidForUse(false);

                if (!await semaphore.WaitAsync(lock_timeout))
                    throw new TimeoutException($"Lock for {nameof(T)} id {id} could not be obtained within timeout period");
            }

            public void ReleaseLock()
            {
                if (!isDestroyed)
                    semaphore.Release();
            }

            private void checkValidForUse(bool shouldBeLocked = true)
            {
                if (isDestroyed) throw new InvalidOperationException("Attempted to use an item which has already been destroyed");
                if (shouldBeLocked && !isLocked) throw new InvalidOperationException("Attempted to destroy a tracked entity without holding a lock");
            }
        }
    }
}
