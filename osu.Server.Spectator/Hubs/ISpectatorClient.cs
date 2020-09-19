using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    public interface ISpectatorClient
    {
        Task UserBeganPlaying(int userId);
    }
}