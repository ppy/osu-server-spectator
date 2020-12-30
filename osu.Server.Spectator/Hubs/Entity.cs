// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;

namespace osu.Server.Spectator.Hubs
{
    public class Entity<T>
        where T : class
    {
        public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1);

        public T? Item;

        public Entity(T? item = null)
        {
            Item = item;
        }
    }
}
