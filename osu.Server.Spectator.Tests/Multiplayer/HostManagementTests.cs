// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
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
            using (var room = await Hub.ActiveRooms.GetForUse(ROOM_ID))
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
            using (var room = await Hub.ActiveRooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Host?.UserID == USER_ID_2);
        }
    }
}
