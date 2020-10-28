// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public class SpectatorHub : Hub<ISpectatorClient>, ISpectatorServer
    {
        private readonly IDistributedCache cache;

        private static readonly ConcurrentDictionary<int, SpectatorState> active_states = new ConcurrentDictionary<int, SpectatorState>();

        public SpectatorHub(IDistributedCache cache)
        {
            this.cache = cache;
        }

        private int currentContextUserId => int.Parse(Context.UserIdentifier);

        public async Task BeginPlaySession(SpectatorState state)
        {
            await updateUserState(state);

            Console.WriteLine($"User {currentContextUserId} beginning play session ({state})");

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(currentContextUserId, state);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            Console.WriteLine($"Receiving frame data ({data.Frames.First()})..");
            await Clients.Group(GetGroupId(currentContextUserId)).UserSentFrames(currentContextUserId, data);
        }

        public async Task EndPlaySession(SpectatorState state)
        {
            Console.WriteLine($"User {currentContextUserId} ending play session ({state})");

            active_states.TryRemove(currentContextUserId, out var _);

            await cache.RemoveAsync(GetStateId(currentContextUserId));
            await Clients.All.UserFinishedPlaying(currentContextUserId, state);
        }

        public async Task StartWatchingUser(int userId)
        {
            Console.WriteLine($"User {currentContextUserId} watching {userId}");

            // send the user's state if exists
            var state = await getStateFromUser(userId);

            if (state != null)
            {
                await Clients.Caller.UserBeganPlaying(userId, state);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public async Task EndWatchingUser(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"User {currentContextUserId} connected!");

            // for now, send *all* player states to users on connect.
            // we don't want this for long, but while the lazer user base is small it should be okay.
            foreach (var kvp in active_states)
                await Clients.Caller.UserBeganPlaying(kvp.Key, kvp.Value);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {currentContextUserId} disconnected!");

            var state = await getStateFromUser(currentContextUserId);

            if (state != null)
            {
                // clean up user on disconnection
                await EndPlaySession(state);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task updateUserState(SpectatorState state)
        {
            active_states.TryRemove(currentContextUserId, out var _);
            active_states.TryAdd(currentContextUserId, state);

            await cache.SetStringAsync(GetStateId(currentContextUserId), JsonConvert.SerializeObject(state));
        }

        private async Task<SpectatorState?> getStateFromUser(int userId)
        {
            var jsonString = await cache.GetStringAsync(GetStateId(userId));

            if (jsonString == null)
                return null;

            // todo: error checking logic?
            var state = JsonConvert.DeserializeObject<SpectatorState>(jsonString);

            return state;
        }

        public static string GetStateId(int userId) => $"state:{userId}";

        public static string GetGroupId(int userId) => $"watch:{userId}";
    }
}
