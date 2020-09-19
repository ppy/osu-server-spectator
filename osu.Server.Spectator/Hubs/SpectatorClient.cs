using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    public class SpectatorClient : ISpectatorClient
    {
        public Task UserBeganPlaying(int userId)
        {
            return Task.CompletedTask;
        }
    }
}