// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class RoomInteropTest : MultiplayerTest
    {
        [Fact]
        public async Task CreateRoom()
        {
            LegacyIO.Setup(io => io.CreateRoomAsync(It.IsAny<int>(), It.IsAny<MultiplayerRoom>()))
                    .ReturnsAsync(() => ROOM_ID);

            await Hub.CreateRoom(new MultiplayerRoom(0));
            LegacyIO.Verify(io => io.CreateRoomAsync(USER_ID, It.IsAny<MultiplayerRoom>()), Times.Once);
            LegacyIO.Verify(io => io.AddUserToRoomAsync(USER_ID, ROOM_ID, It.IsAny<string>()), Times.Once);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                Assert.NotNull(usage.Item);
                Assert.Equal(USER_ID, usage.Item.Users.Single().UserID);
            }
        }

        [Fact]
        public async Task LeaveRoom()
        {
            await Hub.JoinRoom(ROOM_ID);
            LegacyIO.Verify(io => io.RemoveUserFromRoomAsync(USER_ID, ROOM_ID), Times.Never);

            await Hub.LeaveRoom();
            LegacyIO.Verify(io => io.RemoveUserFromRoomAsync(USER_ID, ROOM_ID), Times.Once);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => Hub.GetRoom(ROOM_ID));
        }
    }
}
