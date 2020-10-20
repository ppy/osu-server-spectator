using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class SpectatorFlowTests
    {
        [Fact]
        public async Task NewUserConnection()
        {
            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockClientProxy = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockClientProxy.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.ConnectionId).Returns("1234");

            Mock<IDistributedCache> cache = new Mock<IDistributedCache>();
            
            SpectatorHub hub = new SpectatorHub(cache.Object)
            {
                Context = mockContext.Object,
                Clients = mockClients.Object
            };

            await hub.BeginPlaySession(55);

            mockClients.Verify(clients => clients.All, Times.Once);
            mockClientProxy.Verify(clients => clients.UserBeganPlaying("1234", 55), Times.Once);
        }
    }
}