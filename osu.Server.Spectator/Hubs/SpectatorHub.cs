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
            await Clients.All.UserBeganPlaying(userId);
        }
    }
}