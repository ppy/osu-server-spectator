// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class BeatmapAvailabilityTests : MultiplayerTest
    {
        [Fact]
        public async Task ClientCantChangeAvailabilityWhenNotJoinedRoom()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing()));
        }

        [Fact]
        public async Task AvailabilityChangeBroadcastedOnlyOnChange()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
            Receiver.Verify(b => b.UserBeatmapAvailabilityChanged(USER_ID, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);

            // should not fire a second time.
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
            Receiver.Verify(b => b.UserBeatmapAvailabilityChanged(USER_ID, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);
        }

        [Fact]
        public async Task OnlyClientsInSameRoomReceiveAvailabilityChange()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID_2);

            var user1Availability = BeatmapAvailability.Importing();
            var user2Availability = BeatmapAvailability.Downloading(0.5f);

            SetUserContext(ContextUser);
            await Hub.ChangeBeatmapAvailability(user1Availability);
            using (var room = await Hub.ActiveRooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.Single().BeatmapAvailability.Equals(user1Availability));

            SetUserContext(ContextUser2);
            await Hub.ChangeBeatmapAvailability(user2Availability);
            using (var room2 = await Hub.ActiveRooms.GetForUse(ROOM_ID_2))
                Assert.True(room2.Item?.Users.Single().BeatmapAvailability.Equals(user2Availability));

            Receiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(USER_ID, It.Is<BeatmapAvailability>(b => b.Equals(user1Availability))), Times.Once);
            Receiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(USER_ID_2, It.Is<BeatmapAvailability>(b => b.Equals(user2Availability))), Times.Never);
        }
    }
}
