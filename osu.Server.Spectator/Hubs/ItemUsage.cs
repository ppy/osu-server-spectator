// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// A usage of an item, returned after ensuring locked control.
    /// Should be disposed after usage.
    /// </summary>
    public class ItemUsage<T> : InvokeOnDisposal<EntityStore<T>.TrackedEntity>
        where T : class
    {
        private readonly EntityStore<T>.TrackedEntity entity;

        public T? Item
        {
            get => entity.Item;
            set => entity.Item = value;
        }

        public ItemUsage(in EntityStore<T>.TrackedEntity entity)
            : base(entity, returnLock)
        {
            this.entity = entity;
        }

        /// <summary>
        /// Mark this item as no longer used. Will remove any tracking overhead.
        /// </summary>
        public void Destroy()
        {
            Item = null;
            entity.Destroy();
        }

        private static void returnLock(EntityStore<T>.TrackedEntity entity) => entity.ReleaseLock();
    }
}
