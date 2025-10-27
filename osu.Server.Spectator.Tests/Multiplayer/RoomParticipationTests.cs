// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class RoomParticipationTests : MultiplayerTest
    {
        [Fact]
        public async Task UserCanJoinWithPasswordEvenWhenNotRequired()
        {
            await Hub.CreateRoom(new MultiplayerRoom(ROOM_ID));

            SetUserContext(ContextUser2);
            await Hub.JoinRoomWithPassword(ROOM_ID, "password");
        }

        [Fact]
        public async Task UserCanJoinWithCorrectPassword()
        {
            Database.Setup(db => db.GetRealtimeRoomAsync(It.IsAny<long>()))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(new multiplayer_room
                    {
                        password = "password",
                        user_id = USER_ID
                    });

            await Hub.CreateRoom(new MultiplayerRoom(ROOM_ID) { Settings = { Password = "password" } });

            SetUserContext(ContextUser2);
            await Hub.JoinRoomWithPassword(ROOM_ID, "password");
        }

        [Fact]
        public async Task UserCantJoinWithIncorrectPassword()
        {
            Database.Setup(db => db.GetRealtimeRoomAsync(It.IsAny<long>()))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(new multiplayer_room
                    {
                        password = "password",
                        user_id = USER_ID
                    });

            await Hub.CreateRoom(new MultiplayerRoom(ROOM_ID) { Settings = { Password = "password" } });

            SetUserContext(ContextUser2);
            await Assert.ThrowsAsync<InvalidPasswordException>(() => Hub.JoinRoom(ROOM_ID));
        }

        [Fact]
        public async Task UserCantJoinWhenRestricted()
        {
            Database.Setup(db => db.IsUserRestrictedAsync(It.IsAny<int>())).ReturnsAsync(true);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.JoinRoom(ROOM_ID));

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }

        [Fact]
        public async Task UserCantJoinAlreadyEnded()
        {
            Database.Setup(db => db.GetRealtimeRoomAsync(It.IsAny<long>()))
                    .ReturnsAsync(new multiplayer_room
                    {
                        ends_at = DateTimeOffset.Now.AddMinutes(-5),
                        user_id = USER_ID
                    });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.JoinRoom(ROOM_ID));

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }

        [Fact]
        public async Task UserCantJoinWhenAlreadyJoined()
        {
            await Hub.JoinRoom(ROOM_ID);

            // ensure the same user can't join a room if already in a room.
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.JoinRoom(ROOM_ID));

            // but can join once first leaving.
            await Hub.LeaveRoom();
            await Hub.JoinRoom(ROOM_ID);

            await Hub.LeaveRoom();
        }

        [Fact]
        public async Task LastUserLeavingCausesRoomDisband()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            Database.Verify(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(1));

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            Database.Verify(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(2));

            SetUserContext(ContextUser);
            await Hub.LeaveRoom();

            Database.Verify(db => db.RemoveRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(1));

            // room still exists even though the original host left
            Assert.True(Hub.CheckRoomExists(ROOM_ID));

            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            Database.Verify(db => db.RemoveRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(2));

            // room is gone.
            Assert.False(Hub.CheckRoomExists(ROOM_ID));
        }

        [Fact]
        public async Task LeaveWhenNotAlreadyJoinedIsNoop()
        {
            await Hub.LeaveRoom();

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }

        [Fact]
        public async Task UserJoinLeaveNotifiesOtherUsers()
        {
            await Hub.JoinRoom(ROOM_ID); // join an arbitrary first user (listener).

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            Database.Verify(db => db.AddRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == ROOM_ID), It.Is<MultiplayerRoomUser>(u => u.UserID == USER_ID)), Times.Once);

            var roomUser = new MultiplayerRoomUser(USER_ID_2);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.JoinRoom(ROOM_ID)); // invalid join

            Receiver.Verify(r => r.UserJoined(roomUser), Times.Once);
            Database.Verify(db => db.AddRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == ROOM_ID), It.Is<MultiplayerRoomUser>(u => u.UserID == USER_ID_2)), Times.Once);

            await Hub.LeaveRoom();
            Receiver.Verify(r => r.UserLeft(roomUser), Times.Once);
            Database.Verify(db => db.RemoveRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == ROOM_ID), It.Is<MultiplayerRoomUser>(u => u.UserID == USER_ID_2)), Times.Once);

            await Hub.JoinRoom(ROOM_ID);
            Receiver.Verify(r => r.UserJoined(roomUser), Times.Exactly(2));

            await Hub.LeaveRoom();
            Receiver.Verify(r => r.UserLeft(roomUser), Times.Exactly(2));
        }

        [Fact]
        public async Task UserJoinPreRetrievalFailureCleansUpRoom()
        {
            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID,
                    });

            SetUserContext(ContextUser2); // not the correct user to join the game first; triggers host mismatch failure.
            await Assert.ThrowsAnyAsync<Exception>(() => Hub.JoinRoomExplicit(ROOM_ID));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => Rooms.GetForUse(ROOM_ID));

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }

        [Fact]
        public async Task UserJoinPreJoinFailureCleansUpRoom()
        {
            Database.Setup(db => db.MarkRoomActiveAsync(It.IsAny<MultiplayerRoom>()))
                    .ThrowsAsync(new Exception("error"));

            await Assert.ThrowsAnyAsync<Exception>(() => Hub.JoinRoom(ROOM_ID));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => Rooms.GetForUse(ROOM_ID));

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }

        [Fact]
        public async Task UserJoinPostJoinFailureCleansUpRoomAndUser()
        {
            Database.Setup(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()))
                    .ThrowsAsync(new Exception("error"));

            await Assert.ThrowsAnyAsync<Exception>(() => Hub.JoinRoom(ROOM_ID));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => Rooms.GetForUse(ROOM_ID));

            using (var user = await UserStates.GetForUse(USER_ID))
                Assert.Null(user.Item!.CurrentRoomID);
        }
    }
}
