// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;
using osu.Framework.Allocation;

namespace osu.Server.Spectator.Hubs
{
    public class ItemUsage<T> : InvokeOnDisposal<SemaphoreSlim>
        where T : class
    {
        private readonly Entity<T> entity;

        public T? Item
        {
            get => entity.Item;
            set => entity.Item = value;
        }

        public ItemUsage(in Entity<T> entity)
            : base(entity.Semaphore, returnLock)
        {
            this.entity = entity;
        }

        private static void returnLock(SemaphoreSlim semaphore) => semaphore.Release();
    }
}
