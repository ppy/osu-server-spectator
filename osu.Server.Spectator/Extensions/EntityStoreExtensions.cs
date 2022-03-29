// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Extensions
{
    public static class EntityStoreExtensions
    {
        /// <summary>
        /// Retrieves the connection ID for a user from an <see cref="EntityStore{T}"/>, bypassing <see cref="EntityStore{T}"/> locking.
        /// </summary>
        /// <remarks>
        /// May be used while nested in a locked entity usage (via <see cref="EntityStore{T}.GetForUse"/>).
        /// </remarks>
        /// <param name="store">The user entity store.</param>
        /// <param name="id">The user ID.</param>
        /// <typeparam name="TUserState">The user state.</typeparam>
        /// <returns>The connection ID for the user matching the given <paramref name="id"/>. A non-null return does not mean that the user hasn't been disconnected.</returns>
        public static string? GetConnectionIdForUser<TUserState>(this EntityStore<TUserState> store, long id)
            where TUserState : ClientState
        {
            return store.GetEntityUnsafe(id)?.ConnectionId;
        }
    }
}
