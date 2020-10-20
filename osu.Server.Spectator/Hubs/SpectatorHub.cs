using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    public class SpectatorHub : Hub<ISpectatorClient>, ISpectatorServer
    {
        public async Task BeginPlaySession(int beatmapId)
        {
            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(Context.ConnectionId, beatmapId);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            Console.WriteLine("Receiving frame data..");
            await Clients.Group(getGroupId(Context.ConnectionId)).UserSentFrames(Context.ConnectionId, data);
        }

        public async Task EndPlaySession(int beatmapId)
        {
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

        private static string getGroupId(string userId)
        {
            return $"watch:{userId}";
        }
    }
}