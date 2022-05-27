// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Extensions.ObjectExtensions;
using StatsdClient;

namespace osu.Server.Spectator.Entities
{
    /// <summary>
    /// Tracks and ensures consistency of a collection of entities that have a related permanent ID.
    /// </summary>
    /// <typeparam name="T">The type of the entity being tracked.</typeparam>
    public sealed class EntityStore<T> : IEntityStore
        where T : class
    {
        private readonly Dictionary<long, TrackedEntity> entityMapping = new Dictionary<long, TrackedEntity>();

        private const int lock_timeout = 5000;

        private string statsDPrefix => $"entities.{typeof(T).Name}";

        private bool acceptingNewEntities = true;

        public void StopAcceptingEntities() => acceptingNewEntities = false;

        public int RemainingUsages
        {
            get
            {
                lock (entityMapping)
                    return entityMapping.Count;
            }
        }

        public string EntityName => typeof(T).Name;

        /// <summary>
        /// Retrieves an entity.
        /// </summary>
        /// <remarks>
        /// !!DANGER!! This does not lock on the usage, so it should be used with care assuming the returned value may be invalidated at any point in time.
        /// </remarks>
        /// <param name="id">The ID of the requested entity.</param>
        /// <returns>The entity.</returns>
        public T? GetEntityUnsafe(long id)
        {
            lock (entityMapping)
                return !entityMapping.TryGetValue(id, out var entity) ? null : entity.GetItemUnsafe();
        }

        /// <summary>
        /// Retrieve an entity with a lock for use.
        /// </summary>
        /// <param name="id">The ID of the requested entity.</param>
        /// <param name="createOnMissing">Whether to create a new tracking instance if the entity is not already tracked.</param>
        /// <returns>An <see cref="ItemUsage{T}"/> which allows reading or writing the item. This should be disposed after usage.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if <see cref="createOnMissing"/> was false and the item is not in a tracked state.</exception>
        public async Task<ItemUsage<T>> GetForUse(long id, bool createOnMissing = false)
        {
            int retryCount = 10;

            while (retryCount-- > 0)
            {
                TrackedEntity? item;

                lock (entityMapping)
                {
                    if (!entityMapping.TryGetValue(id, out item))
                    {
                        if (!createOnMissing)
                        {
                            DogStatsd.Increment($"{statsDPrefix}.get-notfound");
                            throw new KeyNotFoundException($"Attempted to get untracked entity {typeof(T)} id {id}");
                        }

                        if (!acceptingNewEntities)
                            throw new HubException("Server is shutting down.");

                        entityMapping[id] = item = new TrackedEntity(id, this);
                        DogStatsd.Gauge($"{statsDPrefix}.total-tracked", entityMapping.Count);
                        DogStatsd.Increment($"{statsDPrefix}.create");
                    }
                }

                try
                {
                    await item.ObtainLockAsync();
                }
                // this may be thrown if the item was destroyed between when we retrieved the item usage and took the lock.
                catch (InvalidOperationException)
                {
                    // if we're looking to create on missing, we should retry the whole process now that we are aware a previous tracked instance was destroyed.
                    if (createOnMissing)
                        continue;

                    // if we're just looking to retrieve and instance, to an external consumer, this should just be handled as the item not being tracked.
                    DogStatsd.Increment($"{statsDPrefix}.get-notfound");
                    throw new KeyNotFoundException($"Attempted to get untracked entity {typeof(T)} id {id}");
                }

                DogStatsd.Increment($"{statsDPrefix}.get");
                return new ItemUsage<T>(item);
            }

            throw new TimeoutException("Could not allocate new entity after multiple retries. Something very bad has happened");
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

            try
            {
                await item.ObtainLockAsync();

                // handles removal and disposal of the semaphore.
                item.Destroy();
                DogStatsd.Increment($"{statsDPrefix}.destroy");
            }
            catch (InvalidOperationException)
            {
                // the item has most likely already been cleaned up if we get here.
            }
        }

        /// <summary>
        /// Get all tracked entities in an unsafe manner. Only read operations should be performed on retrieved entities.
        /// </summary>
        public KeyValuePair<long, T>[] GetAllEntities()
        {
            lock (entityMapping)
            {
                return entityMapping
                       .Where(kvp => kvp.Value.GetItemUnsafe() != null)
                       .Select(entity => new KeyValuePair<long, T>(entity.Key, entity.Value.GetItemUnsafe().AsNonNull()))
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

        private void remove(long id)
        {
            lock (entityMapping)
            {
                entityMapping.Remove(id);

                DogStatsd.Increment($"{statsDPrefix}.removed");
                DogStatsd.Gauge($"{statsDPrefix}.total-tracked", entityMapping.Count);
            }
        }

        public class TrackedEntity
        {
            private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

            private T? item;

            private readonly long id;
            private readonly EntityStore<T> store;

            internal bool IsDestroyed { get; private set; }

            private bool isLocked => semaphore.CurrentCount == 0;

            public TrackedEntity(long id, EntityStore<T> store)
            {
                this.id = id;
                this.store = store;
            }

            public T? GetItemUnsafe() => item;

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
                if (IsDestroyed)
                    return;

                // we should already have a lock when calling destroy.
                checkValidForUse();

                IsDestroyed = true;

                store.remove(id);
                semaphore.Release();
                semaphore.Dispose();
            }

            /// <summary>
            /// Attempt to obtain a lock for this usage.
            /// </summary>
            /// <exception cref="TimeoutException">Throws if the look took too longer to acquire (see <see cref="EntityStore{T}.lock_timeout"/>).</exception>
            /// <exception cref="InvalidOperationException">Thrown if this usage is not in a valid state to perform the requested operation.</exception>
            public async Task ObtainLockAsync()
            {
                checkValidForUse(false);

                if (!await semaphore.WaitAsync(lock_timeout))
                    throw new TimeoutException($"Lock for {typeof(T)} id {id} could not be obtained within timeout period");

                // destroyed state may have changed while waiting for the lock.
                checkValidForUse();
            }

            public void ReleaseLock()
            {
                if (!IsDestroyed)
                    semaphore.Release();
            }

            /// <summary>
            /// Check things are in a valid state to perform an operation.
            /// </summary>
            /// <param name="shouldBeLocked">Whether this usage should be in a locked state at this point.</param>
            /// <exception cref="InvalidOperationException">Thrown if this usage is not in a valid state to perform the requested operation.</exception>
            private void checkValidForUse(bool shouldBeLocked = true)
            {
                if (IsDestroyed) throw new InvalidOperationException("Attempted to use an item which has already been destroyed");
                if (shouldBeLocked && !isLocked) throw new InvalidOperationException("Attempted to access a tracked entity without holding a lock");
            }
        }
    }
}
