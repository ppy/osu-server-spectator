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

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockClientProxy = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockClientProxy.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id);

            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            await hub.BeginPlaySession(88);

            // check all other users were informed that streaming began
            mockClients.Verify(clients => clients.All, Times.Once);
            mockClientProxy.Verify(clients => clients.UserBeganPlaying(streamer_id, 88), Times.Once);

            // check state data was added
            var state = await cache.GetStringAsync("state:1234");
            Assert.Equal("88", state);
        }
    }
}