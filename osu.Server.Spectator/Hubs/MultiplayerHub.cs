// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public class MultiplayerHub : Hub<IMultiplayerClient>, IMultiplayerServer
    {
        private readonly IDistributedCache cache;

        private static readonly ConcurrentDictionary<int, MultiplayerRoomState> active_states = new ConcurrentDictionary<int, MultiplayerRoomState>();

        public MultiplayerHub(IDistributedCache cache)
        {
            this.cache = cache;
        }

        private int currentContextUserId => int.Parse(Context.UserIdentifier);

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"User {currentContextUserId} connected!");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {currentContextUserId} disconnected!");

            await base.OnDisconnectedAsync(exception);
        }
    }
}
