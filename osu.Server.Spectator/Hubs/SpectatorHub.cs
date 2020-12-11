// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs
{
    public class SpectatorHub : StatefulUserHub<ISpectatorClient, SpectatorState>, ISpectatorServer
    {
        public SpectatorHub([NotNull] IDistributedCache cache)
            : base(cache)
        {
        }

        public async Task BeginPlaySession(SpectatorState state)
        {
            await UpdateLocalUserState(state);

            Console.WriteLine($"User {CurrentContextUserId} beginning play session ({state})");

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(CurrentContextUserId, state);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            await Clients.Group(GetGroupId(CurrentContextUserId)).UserSentFrames(CurrentContextUserId, data);
        }

        public async Task EndPlaySession(SpectatorState state)
        {
            Console.WriteLine($"User {CurrentContextUserId} ending play session ({state})");

            await RemoveLocalUserState();
            await Clients.All.UserFinishedPlaying(CurrentContextUserId, state);
        }

        public async Task StartWatchingUser(int userId)
        {
            Console.WriteLine($"User {CurrentContextUserId} watching {userId}");

            // send the user's state if exists
            var state = await GetStateFromUser(userId);

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
            // for now, send *all* player states to users on connect.
            // we don't want this for long, but while the lazer user base is small it should be okay.
            foreach (var kvp in ACTIVE_STATES)
                await Clients.Caller.UserBeganPlaying(kvp.Key, kvp.Value);

            await base.OnConnectedAsync();
        }

        protected override Task OnDisconnectedAsync(Exception exception, SpectatorState? state)
        {
            if (state != null)
                return EndPlaySession(state);

            return base.OnDisconnectedAsync(exception, state);
        }

        public static string GetGroupId(int userId) => $"watch:{userId}";
    }
}
