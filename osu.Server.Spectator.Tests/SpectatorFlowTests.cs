using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class SpectatorFlowTests
    {
        private readonly MemoryDistributedCache cache;
        private readonly SpectatorHub hub;

        public SpectatorFlowTests()
        {
            cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            hub = new SpectatorHub(cache);
        }

        [Fact]
        public async Task NewUserConnectsAndStreamsData()
        {
            const string streamer_id = "1234";
            const int beatmap_id = 88;

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockSender = new Mock<ISpectatorClient>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Caller).Returns(mockSender.Object);
            mockClients.Setup(clients => clients.Group($"watch:{streamer_id}")).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id);

            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            await hub.BeginPlaySession(88);

            // check all other users were informed that streaming began
            mockClients.Verify(clients => clients.All, Times.Once);
            mockReceiver.Verify(clients => clients.UserBeganPlaying(streamer_id, beatmap_id), Times.Once);

            // check state data was added
            var state = await cache.GetStringAsync($"state:{streamer_id}");
            Assert.Equal(beatmap_id.ToString(), state);

            var data = new FrameDataBundle("test");
            
            // check streaming data is propagating to watchers
            await hub.SendFrameData(data);
            mockReceiver.Verify(clients => clients.UserSentFrames(streamer_id, data));
        }
    }
}