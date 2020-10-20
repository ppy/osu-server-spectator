using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    public class SpectatorHub : Hub<ISpectatorClient>, ISpectatorServer
    {
        public async Task BeginPlaySession(int userId)
        {
            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(userId);
        }

        public async Task EndPlaySession(int userId)
        {
            await Clients.All.UserFinishedPlaying(userId);

        }
    }
}