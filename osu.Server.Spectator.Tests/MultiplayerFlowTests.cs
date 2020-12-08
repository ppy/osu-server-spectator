// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Online.RealtimeMultiplayer;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class MultiplayerFlowTests
    {
        private readonly MultiplayerHub hub;

        private const int user_id = 1234;
        private const long room_id = 8888;

        public MultiplayerFlowTests()
        {
            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            hub = new MultiplayerHub(cache);
        }

        [Fact]
        public async Task NewUserConnectsAndJoinsMatch()
        {
            Mock<IGroupManager> mockGroups = new Mock<IGroupManager>();

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.UserIdentifier).Returns(user_id.ToString());

            Mock<IHubCallerClients<IMultiplayerClient>> mockClients = new Mock<IHubCallerClients<IMultiplayerClient>>();
            Mock<IMultiplayerClient> mockReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id))).Returns(mockReceiver.Object);

            hub.Context = mockContext.Object;
            hub.Groups = mockGroups.Object;
            hub.Clients = mockClients.Object;

            Assert.True(await hub.JoinRoom(room_id));

            // ensure the same user can't join a room if already in a room.
            Assert.False(await hub.JoinRoom(room_id));

            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id)), Times.Once);

            await hub.LeaveRoom(room_id);
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id)), Times.Once);

            // ensure we can join a new room after first leaving the last one.
            Assert.True(await hub.JoinRoom(room_id));
            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id)), Times.Exactly(2));

            await hub.LeaveRoom(room_id);
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id)), Times.Exactly(2));
        }
    }
}
