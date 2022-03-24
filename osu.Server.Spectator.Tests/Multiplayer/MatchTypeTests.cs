// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MatchTypeTests : MultiplayerTest
    {
        [Fact]
        public async Task MatchRoomStateUpdatePropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var mockRoomState = new Mock<MatchRoomState>();

                room.MatchState = mockRoomState.Object;

                await Hub.UpdateMatchRoomState(room);

                Receiver.Verify(c => c.MatchRoomStateChanged(mockRoomState.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchEventPropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var mockEvent = new Mock<MatchServerEvent>();

                await Hub.SendMatchEvent(room, mockEvent.Object);

                Receiver.Verify(c => c.MatchEvent(mockEvent.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchUserStateUpdatePropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var roomUsage = await Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);

                var mockRoomState = new Mock<MatchUserState>();

                var user = room.Users.First();

                user.MatchState = mockRoomState.Object;

                await Hub.UpdateMatchUserState(room, user);

                Receiver.Verify(c => c.MatchUserStateChanged(user.UserID, mockRoomState.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchUserRequestForwardsToImplementation()
        {
            Mock<MatchTypeImplementation> typeImplementation;

            await Hub.JoinRoom(ROOM_ID);

            using (var roomUsage = await Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);

                typeImplementation = new Mock<MatchTypeImplementation>(room, Hub);
                room.MatchTypeImplementation = typeImplementation.Object;
            }

            var mockRequest = new Mock<MatchUserRequest>();

            await Hub.SendMatchRequest(mockRequest.Object);

            using (var roomUsage = await Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);
                typeImplementation.Verify(r => r.HandleUserRequest(room.Users.First(), mockRequest.Object), Times.Once());
            }
        }

        [Fact]
        public async Task ChangeMatchType()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // initially default
                Assert.Equal(MatchType.HeadToHead, room.Settings.MatchType);
            }

            await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = MatchType.TeamVersus });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.MatchTypeImplementation is TeamVersus);
                Assert.Equal(MatchType.TeamVersus, room.Settings.MatchType);

                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }

            Receiver.Verify(r => r.MatchUserStateChanged(USER_ID, It.IsAny<TeamVersusUserState>()), Times.Once);
        }

        [Fact]
        public async Task JoinRoomWithTypeCreatesCorrectInstance()
        {
            Database.Setup(db => db.GetRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(new multiplayer_room
                    {
                        type = database_match_type.team_versus,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID,
                    });

            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(MatchType.TeamVersus, room.Settings.MatchType);
                Assert.IsType<TeamVersus>(room.MatchTypeImplementation);
            }
        }

        [Fact]
        public async Task ExistingUserInformedOfNewUserStateCorrectOrder()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = MatchType.TeamVersus });

            int callOrder = 0;
            Receiver.Setup(r => r.UserJoined(It.IsAny<MultiplayerRoomUser>())).Callback(() => Assert.Equal(0, callOrder++));
            Receiver.Setup(r => r.MatchUserStateChanged(It.IsAny<int>(), It.IsAny<MatchUserState>())).Callback(() => Assert.Equal(1, callOrder++));

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            // ensure the calls actually happened.
            Assert.Equal(2, callOrder);
        }
    }
}
