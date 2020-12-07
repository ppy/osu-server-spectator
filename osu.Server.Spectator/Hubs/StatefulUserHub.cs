// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public abstract class StatefulUserHub<TClient, TUserState> : Hub<TClient>
        where TUserState : class
        where TClient : class
    {
        protected readonly IDistributedCache Cache;

        protected static readonly ConcurrentDictionary<int, TUserState> ACTIVE_STATES = new ConcurrentDictionary<int, TUserState>();

        protected StatefulUserHub(IDistributedCache cache)
        {
            this.Cache = cache;
        }

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

        protected async Task UpdateLocalUserState(TUserState state)
        {
            ACTIVE_STATES.TryRemove(CurrentContextUserId, out var _);
            ACTIVE_STATES.TryAdd(CurrentContextUserId, state);

            await Cache.SetStringAsync(GetStateId(CurrentContextUserId), JsonConvert.SerializeObject(state));
        }

        protected Task<TUserState?> GetLocalUserState() => GetStateFromUser(CurrentContextUserId);

        protected async Task RemoveLocalUserState()
        {
            ACTIVE_STATES.TryRemove(CurrentContextUserId, out var _);

            await Cache.RemoveAsync(GetStateId(CurrentContextUserId));
        }

        protected async Task<TUserState?> GetStateFromUser(int userId)
        {
            var jsonString = await Cache.GetStringAsync(GetStateId(userId));

            if (jsonString == null)
                return null;

            // todo: error checking logic?
            var state = JsonConvert.DeserializeObject<TUserState>(jsonString);

            return state;
        }

        public static string GetStateId(int userId) => $"state-{nameof(TClient)}:{userId}";
    }
}
