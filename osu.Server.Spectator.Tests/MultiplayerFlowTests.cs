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

        private readonly Mock<IMultiplayerClient> mockReceiver;

        public MultiplayerFlowTests()
        {
            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            hub = new MultiplayerHub(cache);

            Mock<IGroupManager> mockGroups = new Mock<IGroupManager>();

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.UserIdentifier).Returns(user_id.ToString());

            Mock<IHubCallerClients<IMultiplayerClient>> mockClients = new Mock<IHubCallerClients<IMultiplayerClient>>();
            mockReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id))).Returns(mockReceiver.Object);

            hub.Context = mockContext.Object;
            hub.Groups = mockGroups.Object;
            hub.Clients = mockClients.Object;
        }

        [Fact]
        public async Task UserCantJoinWhenAlreadyJoined()
        {
            Assert.True(await hub.JoinRoom(room_id));

            // ensure the same user can't join a room if already in a room.
            Assert.False(await hub.JoinRoom(room_id));

            // but can join once first leaving.
            Assert.True(await hub.LeaveRoom(room_id));
            Assert.True(await hub.JoinRoom(room_id));
            Assert.True(await hub.LeaveRoom(room_id));
        }

        [Fact]
        public async Task UserJoinLeaveNotifiesOtherUsers()
        {
            await hub.JoinRoom(room_id);
            await hub.JoinRoom(room_id); // invalid join

            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id)), Times.Once);

            await hub.LeaveRoom(room_id);
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id)), Times.Once);

            await hub.JoinRoom(room_id);
            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id)), Times.Exactly(2));

            await hub.LeaveRoom(room_id);
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id)), Times.Exactly(2));
        }

        [Fact]
        public async Task UserCantChangeSettingsWhenNotJoinedRoom()
        {
            await Assert.ThrowsAsync<MultiplayerHub.NotJoinedRoomException>(() => hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task RoomSettingsUpdateNotifiesOtherUsers()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeSettings(new MultiplayerRoomSettings());
        }
    }
}
