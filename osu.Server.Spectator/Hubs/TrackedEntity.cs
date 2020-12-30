// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;

namespace osu.Server.Spectator.Hubs
{
    public class TrackedEntity<T>
        where T : class
    {
        public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

        public T? Item;

        public bool IsDestroyed { get; private set; }

        /// <summary>
        /// Mark this item as no longer used. Will remove any tracking overhead.
        /// </summary>
        public void Destroy()
        {
            IsDestroyed = true;
        }
    }
}
