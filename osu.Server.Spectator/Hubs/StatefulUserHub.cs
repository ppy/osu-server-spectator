// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public abstract class StatefulUserHub<TClient, TUserState> : Hub<TClient>
        where TUserState : class
        where TClient : class
    {
        protected static readonly EntityStore<TUserState> ACTIVE_STATES = new EntityStore<TUserState>();

        protected StatefulUserHub(IDistributedCache cache)
        {
        }

        protected static KeyValuePair<long, TUserState?>[] GetAllStates() => ACTIVE_STATES.GetAllStates();

        /// <summary>
        /// The osu! user id for the currently processing context.
        /// </summary>
        protected int CurrentContextUserId => int.Parse(Context.UserIdentifier);

        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"User {CurrentContextUserId} connected!");

            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a user disconnected, providing their last state.
        /// </summary>
        /// <param name="exception">A potential error which caused the disconnection.</param>
        /// <param name="state">The last user state. May be null. This is automatically cleared on disconnection.</param>
        protected virtual Task OnDisconnectedAsync(Exception exception, TUserState? state) => Task.CompletedTask;

        public sealed override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {CurrentContextUserId} disconnected!");

            var state = await GetLocalUserState();

            await OnDisconnectedAsync(exception, state);

            // clean up user on disconnection
            if (state != null) await RemoveLocalUserState();

            await base.OnDisconnectedAsync(exception);
        }

        protected Task<ItemUsage<TUserState>> GetLocalUserState() =>
            GetStateFromUser(CurrentContextUserId);

        protected Task<ItemUsage<TUserState>> GetStateFromUser(int userId) =>
            ACTIVE_STATES.GetForUse(userId);

        public static string GetStateId(int userId) => $"state-{typeof(TClient)}:{userId}";

        public static void Reset() => ACTIVE_STATES.Clear();
    }
}
