using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    public class SpectatorHub : Hub<ISpectatorClient>, ISpectatorServer
    {
        private readonly IDistributedCache cache;

        public SpectatorHub(IDistributedCache cache)
        {
            this.cache = cache;
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var state = await getStateFromUser();

            if (state != null)
            {
                // clean up user on disconnection
                await EndPlaySession(int.Parse(state));
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task BeginPlaySession(int beatmapId)
        {
            await cache.SetStringAsync(getStateId(Context.ConnectionId), beatmapId.ToString());

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(Context.ConnectionId, beatmapId);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            var state = await getStateFromUser();

            Console.WriteLine($"Receiving frame data (beatmap {state})..");
            await Clients.Group(getGroupId(Context.ConnectionId)).UserSentFrames(Context.ConnectionId, data);
        }

        public async Task EndPlaySession(int beatmapId)
        {
            await cache.RemoveAsync(getStateId(Context.ConnectionId));
            await Clients.All.UserFinishedPlaying(Context.ConnectionId, beatmapId);
        }

        public async Task StartWatchingUser(string userId)
        {
            Console.WriteLine($"User {Context.ConnectionId} watching {userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, getGroupId(userId));
        }

        public async Task EndWatchingUser(string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, getGroupId(userId));
        }

        private async Task<string> getStateFromUser() => await cache.GetStringAsync(getStateId(Context.ConnectionId));

        private static string getStateId(string userId) => $"state:{userId}";
        private static string getGroupId(string userId) => $"watch:{userId}";
    }
}