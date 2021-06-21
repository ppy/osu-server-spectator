// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs
{
    public class SpectatorHub : StatefulUserHub<ISpectatorClient, SpectatorClientState>, ISpectatorServer
    {
        public SpectatorHub(IDistributedCache cache)
            : base(cache)
        {
        }

        public async Task BeginPlaySession(SpectatorState state)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                usage.Item ??= new SpectatorClientState(Context.ConnectionId, CurrentContextUserId);
                usage.Item.State = state;
            }

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(CurrentContextUserId, state);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            await Clients.Group(GetGroupId(CurrentContextUserId)).UserSentFrames(CurrentContextUserId, data);
        }

        public async Task EndPlaySession(SpectatorState state)
        {
            using (var usage = await GetOrCreateLocalUserState())
                usage.Destroy();

            await endPlaySession(CurrentContextUserId, state);
        }

        public async Task StartWatchingUser(int userId)
        {
            Log($"Watching {userId}");

            try
            {
                SpectatorState? spectatorState;

                // send the user's state if exists
                using (var usage = await GetStateFromUser(userId))
                    spectatorState = usage.Item?.State;

                if (spectatorState != null)
                    await Clients.Caller.UserBeganPlaying(userId, spectatorState);
            }
            catch (KeyNotFoundException)
            {
                // user isn't tracked.
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
            foreach (var kvp in GetAllStates())
                await Clients.Caller.UserBeganPlaying((int)kvp.Key, kvp.Value.State);

            await base.OnConnectedAsync();
        }

        protected override async Task CleanUpState(SpectatorClientState state)
        {
            if (state.State != null)
                await endPlaySession(state.UserId, state.State);

            await base.CleanUpState(state);
        }

        public static string GetGroupId(int userId) => $"watch:{userId}";

        private async Task endPlaySession(int userId, SpectatorState state)
        {
            await Clients.All.UserFinishedPlaying(userId, state);
        }
    }
}
