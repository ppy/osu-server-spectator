using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs
{
    public interface ISpectatorServer
    {
        Task BeginPlaySession(int beatmapId);
        Task EndPlaySession(int beatmapId);
    }
}