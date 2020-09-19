using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    public interface ISpectatorServer
    {
        Task BeginPlaySession(int userId);
    }
}