// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class HostManagementTests : MultiplayerTest
    {
        [Fact]
        public async Task FirstUserBecomesHost()
        {
            var room = await Hub.JoinRoom(ROOM_ID);
            Assert.True(room.Host?.UserID == USER_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            Assert.True(room.Host?.UserID == USER_ID);
        }

        [Fact]
        public async Task HostTransfer()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Hub.TransferHost(USER_ID_2);

            Receiver.Verify(r => r.HostChanged(USER_ID_2), Times.Once);
            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Host?.UserID == USER_ID_2);
        }

        [Fact]
        public async Task HostLeavingCausesHostTransfer()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Hub.LeaveRoom();

            Receiver.Verify(r => r.HostChanged(USER_ID_2), Times.Once);
            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Host?.UserID == USER_ID_2);
        }

        [Fact]
        public async Task HostKicksUser()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            var kickedUserReceiver = new Mock<IMultiplayerClient>();
            Clients.Setup(clients => clients.Client(USER_ID_2.ToString())).Returns(kickedUserReceiver.Object);

            SetUserContext(ContextUser);
            await Hub.KickUser(USER_ID_2);

            // other players received the event.
            Receiver.Verify(r => r.UserKicked(It.Is<MultiplayerRoomUser>(u => u.UserID == USER_ID_2)), Times.Once);

            // the kicked user received the event.
            kickedUserReceiver.Verify(r => r.UserKicked(It.Is<MultiplayerRoomUser>(u => u.UserID == USER_ID_2)), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.All(u => u.UserID != USER_ID_2));
        }

        [Fact]
        public async Task HostAttemptsToKickSelf()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.KickUser(USER_ID));

            Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Never);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.Any(u => u.UserID != USER_ID_2));
        }

        [Fact]
        public async Task NonHostAttemptsToKickUser()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<NotHostException>(() => Hub.KickUser(USER_ID));

            Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Never);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.Any(u => u.UserID != USER_ID_2));
        }
    }
}
