// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class MultiplayerFlowTests
    {
        private readonly TestMultiplayerHub hub;

        private const int user_id = 1234;
        private const int user_id_2 = 2345;

        private const long room_id = 8888;
        private const long room_id_2 = 9999;

        private readonly Mock<IDatabaseFactory> mockDatabaseFactory;
        private readonly Mock<IDatabaseAccess> mockDatabase;

        private readonly Mock<IMultiplayerClient> mockReceiver;
        private readonly Mock<IMultiplayerClient> mockGameplayReceiver;

        private readonly Mock<HubCallerContext> mockContextUser1;
        private readonly Mock<HubCallerContext> mockContextUser2;

        private readonly Mock<IHubCallerClients<IMultiplayerClient>> mockClients;
        private readonly Mock<IGroupManager> mockGroups;
        private readonly Mock<IMultiplayerClient> mockCaller;

        public MultiplayerFlowTests()
        {
            MultiplayerHub.Reset();

            mockDatabaseFactory = new Mock<IDatabaseFactory>();
            mockDatabase = new Mock<IDatabaseAccess>();
            setUpMockDatabase();

            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            hub = new TestMultiplayerHub(cache, mockDatabaseFactory.Object);

            mockClients = new Mock<IHubCallerClients<IMultiplayerClient>>();
            mockGroups = new Mock<IGroupManager>();

            mockContextUser1 = new Mock<HubCallerContext>();
            mockContextUser1.Setup(context => context.UserIdentifier).Returns(user_id.ToString());
            mockContextUser1.Setup(context => context.ConnectionId).Returns(user_id.ToString());

            mockContextUser2 = new Mock<HubCallerContext>();
            mockContextUser2.Setup(context => context.UserIdentifier).Returns(user_id_2.ToString());
            mockContextUser2.Setup(context => context.ConnectionId).Returns(user_id_2.ToString());

            mockReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id, false))).Returns(mockReceiver.Object);

            mockGameplayReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id, true))).Returns(mockGameplayReceiver.Object);

            var mockReceiver2 = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id_2, false))).Returns(mockReceiver2.Object);

            mockCaller = new Mock<IMultiplayerClient>();
            mockClients.Setup(client => client.Caller).Returns(mockCaller.Object);

            hub.Groups = mockGroups.Object;
            hub.Clients = mockClients.Object;

            setUserContext(mockContextUser1);
        }

        private void setUpMockDatabase()
        {
            mockDatabaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);
            mockDatabase.Setup(db => db.GetRoomAsync(room_id))
                        .ReturnsAsync(new multiplayer_room
                        {
                            ends_at = DateTimeOffset.Now.AddMinutes(5),
                            user_id = user_id
                        });
            mockDatabase.Setup(db => db.GetRoomAsync(room_id_2))
                        .ReturnsAsync(new multiplayer_room
                        {
                            ends_at = DateTimeOffset.Now.AddMinutes(5),
                            user_id = user_id_2
                        });

            mockDatabase.Setup(db => db.GetCurrentPlaylistItemAsync(It.IsAny<long>()))
                        .ReturnsAsync(new multiplayer_playlist_item
                        {
                            beatmap_id = 1234
                        });
            mockDatabase.Setup(db => db.GetBeatmapChecksumAsync(It.IsAny<int>()))
                        .ReturnsAsync("checksum"); // doesn't matter if bogus, just needs to be non-empty.

            mockDatabase.Setup(db => db.GetPlaylistItemFromRoomAsync(It.IsAny<long>(), It.IsAny<long>()))
                        .Returns<long, long>((roomId, playlistItemId) => Task.FromResult<multiplayer_playlist_item?>(new multiplayer_playlist_item
                        {
                            id = playlistItemId,
                            room_id = roomId,
                            beatmap_id = 1234,
                        }));
        }

        [Fact]
        public async Task RoomHasNewPlaylistItemAfterMatchStart()
        {
            long playlistItemId = (await hub.JoinRoom(room_id)).Settings.PlaylistItemId;
            long expectedPlaylistItemId = playlistItemId + 1;

            mockDatabase.Setup(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()))
                        .ReturnsAsync(() => expectedPlaylistItemId);

            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();
            await hub.ChangeState(MultiplayerUserState.Loaded);
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(expectedPlaylistItemId, room.Settings.PlaylistItemId);
                mockReceiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task ServerDoesNotAcceptClientPlaylistItemId()
        {
            await hub.JoinRoom(room_id);

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                BeatmapChecksum = "checksum",
                PlaylistItemId = 1
            };

            await hub.ChangeSettings(testSettings);

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(0, room.Settings.PlaylistItemId);
                mockReceiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        #region Host assignment and transfer

        [Fact]
        public async Task FirstUserBecomesHost()
        {
            var room = await hub.JoinRoom(room_id);
            Assert.True(room.Host?.UserID == user_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            Assert.True(room.Host?.UserID == user_id);
        }

        [Fact]
        public async Task HostTransfer()
        {
            setUserContext(mockContextUser1);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.TransferHost(user_id_2);

            mockReceiver.Verify(r => r.HostChanged(user_id_2), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.True(room.Item?.Host?.UserID == user_id_2);
        }

        [Fact]
        public async Task HostLeavingCausesHostTransfer()
        {
            setUserContext(mockContextUser1);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.LeaveRoom();

            mockReceiver.Verify(r => r.HostChanged(user_id_2), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.True(room.Item?.Host?.UserID == user_id_2);
        }

        #endregion

        #region Joining and leaving

        [Fact]
        public async Task UserCanJoinWithPasswordEvenWhenNotRequired()
        {
            await hub.JoinRoom(room_id, "password");
        }

        [Fact]
        public async Task UserCanJoinWithCorrectPassword()
        {
            mockDatabase.Setup(db => db.GetRoomAsync(It.IsAny<long>()))
                        .ReturnsAsync(new multiplayer_room
                        {
                            password = "password",
                            user_id = user_id
                        });

            await hub.JoinRoom(room_id, "password");
        }

        [Fact]
        public async Task UserCantJoinWithIncorrectPassword()
        {
            mockDatabase.Setup(db => db.GetRoomAsync(It.IsAny<long>()))
                        .ReturnsAsync(new multiplayer_room
                        {
                            password = "password",
                            user_id = user_id
                        });

            await Assert.ThrowsAsync<InvalidPasswordException>(() => hub.JoinRoom(room_id));
        }

        [Fact]
        public async Task UserCantJoinWhenRestricted()
        {
            mockDatabase.Setup(db => db.IsUserRestrictedAsync(It.IsAny<int>())).ReturnsAsync(true);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.JoinRoom(room_id));

            // ensure no state was left behind.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserCantJoinAlreadyEnded()
        {
            mockDatabase.Setup(db => db.GetRoomAsync(It.IsAny<long>()))
                        .ReturnsAsync(new multiplayer_room
                        {
                            ends_at = DateTimeOffset.Now.AddMinutes(-5),
                            user_id = user_id
                        });

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.JoinRoom(room_id));

            // ensure no state was left behind.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserCantJoinWhenAlreadyJoined()
        {
            await hub.JoinRoom(room_id);

            // ensure the same user can't join a room if already in a room.
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.JoinRoom(room_id));

            // but can join once first leaving.
            await hub.LeaveRoom();
            await hub.JoinRoom(room_id);

            await hub.LeaveRoom();
        }

        [Fact]
        public async Task LastUserLeavingCausesRoomDisband()
        {
            setUserContext(mockContextUser1);
            await hub.JoinRoom(room_id);

            mockDatabase.Verify(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(1));

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            mockDatabase.Verify(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(2));

            setUserContext(mockContextUser1);
            await hub.LeaveRoom();

            mockDatabase.Verify(db => db.RemoveRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(1));

            // room still exists even though the original host left
            Assert.True(hub.CheckRoomExists(room_id));

            setUserContext(mockContextUser2);
            await hub.LeaveRoom();

            mockDatabase.Verify(db => db.RemoveRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()), Times.Exactly(2));

            // room is gone.
            Assert.False(hub.CheckRoomExists(room_id));
        }

        [Fact]
        public async Task UserCantLeaveWhenNotAlreadyJoined()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => hub.LeaveRoom());

            // ensure no state was left behind.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserJoinLeaveNotifiesOtherUsers()
        {
            await hub.JoinRoom(room_id); // join an arbitrary first user (listener).

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            mockDatabase.Verify(db => db.AddRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == room_id), It.Is<MultiplayerRoomUser>(u => u.UserID == user_id)), Times.Once);

            var roomUser = new MultiplayerRoomUser(user_id_2);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.JoinRoom(room_id)); // invalid join

            mockReceiver.Verify(r => r.UserJoined(roomUser), Times.Once);
            mockDatabase.Verify(db => db.AddRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == room_id), It.Is<MultiplayerRoomUser>(u => u.UserID == user_id_2)), Times.Once);

            await hub.LeaveRoom();
            mockReceiver.Verify(r => r.UserLeft(roomUser), Times.Once);
            mockDatabase.Verify(db => db.RemoveRoomParticipantAsync(It.Is<MultiplayerRoom>(r => r.RoomID == room_id), It.Is<MultiplayerRoomUser>(u => u.UserID == user_id_2)), Times.Once);

            await hub.JoinRoom(room_id);
            mockReceiver.Verify(r => r.UserJoined(roomUser), Times.Exactly(2));

            await hub.LeaveRoom();
            mockReceiver.Verify(r => r.UserLeft(roomUser), Times.Exactly(2));
        }

        [Fact]
        public async Task UserJoinPreRetrievalFailureCleansUpRoom()
        {
            setUserContext(mockContextUser2); // not the correct user to join the game first; triggers host mismatch failure.
            await Assert.ThrowsAnyAsync<Exception>(() => hub.JoinRoom(room_id));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.RoomStore.GetForUse(room_id));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserJoinPreJoinFailureCleansUpRoom()
        {
            mockDatabase.Setup(db => db.MarkRoomActiveAsync(It.IsAny<MultiplayerRoom>()))
                        .ThrowsAsync(new Exception("error"));
            await Assert.ThrowsAnyAsync<Exception>(() => hub.JoinRoom(room_id));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.RoomStore.GetForUse(room_id));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserJoinPostJoinFailureCleansUpRoomAndUser()
        {
            mockDatabase.Setup(db => db.AddRoomParticipantAsync(It.IsAny<MultiplayerRoom>(), It.IsAny<MultiplayerRoomUser>()))
                        .ThrowsAsync(new Exception("error"));
            await Assert.ThrowsAnyAsync<Exception>(() => hub.JoinRoom(room_id));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.RoomStore.GetForUse(room_id));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        #endregion

        #region User State

        [Fact]
        public async Task UserStateChangeNotifiesOtherUsers()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeState(MultiplayerUserState.Ready);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Ready), Times.Once);
        }

        [Theory]
        [InlineData(MultiplayerUserState.WaitingForLoad)]
        [InlineData(MultiplayerUserState.Playing)]
        [InlineData(MultiplayerUserState.Results)]
        public async Task UserCantChangeStateToReservedStates(MultiplayerUserState reservedState)
        {
            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateChangeException>(() => hub.ChangeState(reservedState));
        }

        [Fact]
        public async Task StartingMatchWithNoReadyUsersFails()
        {
            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.StartMatch());
        }

        [Fact]
        public async Task StartingMatchWithHostNotReadyFails()
        {
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser1);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.StartMatch());
        }

        [Fact]
        public async Task StartingAlreadyStartedMatchFails()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);

            await hub.StartMatch();

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.StartMatch());
        }

        [Fact]
        public async Task AllUsersBackingOutFromLoadCancelsTransitionToPlay()
        {
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await hub.ChangeState(MultiplayerUserState.Idle);
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
        }

        [Fact]
        public async Task OnlyReadiedUpUsersTransitionToPlay()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.StartMatch();

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.WaitingForLoad);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }

            await hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Playing);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }
        }

        [Fact]
        public async Task UserDisconnectsDuringGameplayUpdatesRoomState()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser1);
            await hub.StartMatch();

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            }

            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Loaded);
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
            }

            // first user exits gameplay
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);

            // second user gets disconnected
            setUserContext(mockContextUser2);
            await hub.LeaveRoom();

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
        }

        [Fact]
        public async Task OnlyFinishedUsersTransitionToResults()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);

            await hub.StartMatch();
            await hub.ChangeState(MultiplayerUserState.Loaded);

            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            verifyRemovedFromGameplayGroup(mockContextUser1, room_id);
            verifyRemovedFromGameplayGroup(mockContextUser2, room_id, false);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Results);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }
        }

        [Fact]
        public async Task OnlyReadyPlayersAreAddedToAndRemovedFromGameplayGroup()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);

            await hub.StartMatch();
            await hub.ChangeState(MultiplayerUserState.Loaded);

            verifyAddedToGameplayGroup(mockContextUser1, room_id);
            verifyAddedToGameplayGroup(mockContextUser2, room_id, false);

            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            verifyRemovedFromGameplayGroup(mockContextUser1, room_id);
            verifyRemovedFromGameplayGroup(mockContextUser2, room_id, false);
        }

        /// <summary>
        /// Tests a full game flow with one user in the room.
        /// </summary>
        [Fact]
        public async Task SingleUserMatchFlow()
        {
            await hub.JoinRoom(room_id);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // some users enter a ready state.
            await hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }

            // host requests the start of the match.
            await hub.StartMatch();

            // server requests the all users start loading.
            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.WaitingForLoad), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))

                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));

            // all users finish loading.
            await hub.ChangeState(MultiplayerUserState.Loaded);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Playing), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);

            // server requests users start playing.
            mockReceiver.Verify(r => r.MatchStarted(), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));

            // all users finish playing.
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);

            // server lets players know that results are ready for consumption (all players have finished).
            mockReceiver.Verify(r => r.ResultsReady(), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Results), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));

            // players return back to idle state as they please.
            await hub.ChangeState(MultiplayerUserState.Idle);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
        }

        /// <summary>
        /// Tests a full game flow with two users in the room.
        /// Focuses on the interactions during loading sections.
        /// </summary>
        [Fact]
        public async Task MultiUserMatchFlow()
        {
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // both users become ready.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Ready);
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            // host requests the start of the match.
            setUserContext(mockContextUser1);
            await hub.StartMatch();

            // server requests the all users start loading.
            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.WaitingForLoad), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.WaitingForLoad), Times.Once);

            // first user finishes loading.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Loaded);

            // room is still waiting for second user to load.
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);
            mockReceiver.Verify(r => r.MatchStarted(), Times.Never);

            // second user finishes loading, which triggers gameplay to start.
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
                mockReceiver.Verify(r => r.MatchStarted(), Times.Once);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Playing), Times.Once);
                mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Playing), Times.Once);
            }

            // first user finishes playing.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                // room is still waiting for second user to finish playing.
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
                mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.FinishedPlay), Times.Once);
                mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Playing), Times.Once);
                mockReceiver.Verify(r => r.ResultsReady(), Times.Never);
            }

            // second user finishes playing.
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                // server lets players know that results are ready for consumption (all players have finished).
                mockReceiver.Verify(r => r.ResultsReady(), Times.Once);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));
                mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Results), Times.Once);
                mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Results), Times.Once);

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }
        }

        [Fact]
        public async void NotReadyUsersDontGetLoadRequest()
        {
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // one user enters a ready state.
            await hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Idle));
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Ready));

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }

            // host requests the start of the match.
            await hub.StartMatch();

            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            mockReceiver.Verify(r => r.LoadRequested(), Times.Never);

            using (var room = await hub.RoomStore.GetForUse(room_id))
            {
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.WaitingForLoad));
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Idle));
            }
        }

        #endregion

        #region Beatmap Availability

        [Fact]
        public async Task ClientCantChangeAvailabilityWhenNotJoinedRoom()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing()));
        }

        [Fact]
        public async Task AvailabilityChangeBroadcastedOnlyOnChange()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
            mockReceiver.Verify(b => b.UserBeatmapAvailabilityChanged(user_id, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);

            // should not fire a second time.
            await hub.ChangeBeatmapAvailability(BeatmapAvailability.Importing());
            mockReceiver.Verify(b => b.UserBeatmapAvailabilityChanged(user_id, It.Is<BeatmapAvailability>(b2 => b2.State == DownloadState.Importing)), Times.Once);
        }

        [Fact]
        public async Task OnlyClientsInSameRoomReceiveAvailabilityChange()
        {
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id_2);

            var user1Availability = BeatmapAvailability.Importing();
            var user2Availability = BeatmapAvailability.Downloading(0.5f);

            setUserContext(mockContextUser1);
            await hub.ChangeBeatmapAvailability(user1Availability);
            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.True(room.Item?.Users.Single().BeatmapAvailability.Equals(user1Availability));

            setUserContext(mockContextUser2);
            await hub.ChangeBeatmapAvailability(user2Availability);
            using (var room2 = await hub.RoomStore.GetForUse(room_id_2))
                Assert.True(room2.Item?.Users.Single().BeatmapAvailability.Equals(user2Availability));

            mockReceiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(user_id, It.Is<BeatmapAvailability>(b => b.Equals(user1Availability))), Times.Once);
            mockReceiver.Verify(c1 => c1.UserBeatmapAvailabilityChanged(user_id_2, It.Is<BeatmapAvailability>(b => b.Equals(user2Availability))), Times.Never);
        }

        #endregion

        #region Mod validation

        [Fact]
        public async Task HostCanSetIncompatibleAllowedModsCombination()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                AllowedMods = new[]
                {
                    // setting an incompatible combination should be allowed.
                    // will be enforced at the point of a user choosing from the allowed mods.
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModApproachDifferent()),
                },
            });
        }

        [Fact]
        public async Task HostSetsInvalidAllowedModsForRulesetThrows()
        {
            await hub.JoinRoom(room_id);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                AllowedMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task HostSetsInvalidRequiredModsCombinationThrows()
        {
            await hub.JoinRoom(room_id);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                    new APIMod(new OsuModApproachDifferent()),
                },
            }));
        }

        [Fact]
        public async Task HostSetsInvalidRequiredModsForRulesetThrows()
        {
            await hub.JoinRoom(room_id);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task HostSetsInvalidRequiredAllowedModsCombinationThrows()
        {
            await hub.JoinRoom(room_id);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                },
                AllowedMods = new[]
                {
                    // allowed and required mods should always be cross-compatible.
                    new APIMod(new OsuModApproachDifferent()),
                },
            }));
        }

        [Fact(Skip = "needs dedupe check logic somewhere")]
        public async Task HostSetsOverlappingRequiredAllowedMods()
        {
            await hub.JoinRoom(room_id);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RequiredMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                },
                AllowedMods = new[]
                {
                    // if a mod is in RequiredMods it shouldn't also be in AllowedMods.
                    new APIMod(new OsuModFlashlight()),
                },
            }));
        }

        [Fact]
        public async Task UserChangesMods()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            var setMods = new[] { new APIMod(new OsuModApproachDifferent()) };
            await hub.ChangeUserMods(setMods);

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(setMods, room.Users.First().Mods);
            }

            setMods = new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModFlashlight()) };
            await hub.ChangeUserMods(setMods);

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(setMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsInvalidModCombinationThrows()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            await hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModHidden()) }));

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(originalMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsDisallowedModsThrows()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                RulesetID = 2,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new CatchModHidden()),
                },
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeUserMods(new[]
            {
                new APIMod(new CatchModHidden()),
                // this should cause the complete setting change to fail, including the hidden mod application.
                new APIMod(new CatchModDaycore())
            }));

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Empty(room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsInvalidModsForRulesetThrows()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                RulesetID = 2,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new CatchModHidden()),
                },
            });

            await hub.ChangeUserMods(new[] { new APIMod(new CatchModHidden()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) }));

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(originalMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task ChangingDisallowedModsRemovesUserMods()
        {
            await hub.JoinRoom(room_id);

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent()),
                    new APIMod(new OsuModFlashlight())
                },
            });

            await hub.ChangeUserMods(new[]
            {
                new APIMod(new OsuModApproachDifferent()),
                new APIMod(new OsuModFlashlight())
            });

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(2, room.Users.First().Mods.Count());
            }

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[] { new APIMod(new OsuModFlashlight()) },
            });

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Single(room.Users.First().Mods);
                Assert.True(room.Users.First().Mods.Single().Acronym == "FL");
            }
        }

        [Fact]
        public async Task ChangingRulesetRemovesInvalidUserMods()
        {
            await hub.JoinRoom(room_id);

            var roomSettings = new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent())
                },
            };

            await hub.ChangeSettings(roomSettings);

            await hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.NotEmpty(room.Users.First().Mods);
            }

            await hub.ChangeSettings(new MultiplayerRoomSettings
            {
                RulesetID = 2,
                BeatmapChecksum = "checksum",
            });

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Empty(room.Users.First().Mods);
            }
        }

        #endregion

        #region Room Settings

        [Fact]
        public async Task ChangingSettingsUpdatesModel()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                BeatmapChecksum = "checksum"
            };

            await hub.JoinRoom(room_id);
            await hub.ChangeSettings(testSettings);

            using (var usage = hub.GetRoom(room_id))
            {
                var room = usage.Item;

                Debug.Assert(room != null);
                Assert.Equal(testSettings.Name, room.Settings.Name);
            }
        }

        [Fact]
        public async Task ChangingSettingsMarksReadyUsersAsIdle()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                BeatmapChecksum = "checksum"
            };

            await hub.JoinRoom(room_id);

            MultiplayerRoom? room;

            using (var usage = hub.GetRoom(room_id))
            {
                // unsafe, but just for tests.
                room = usage.Item;
                Debug.Assert(room != null);
            }

            await hub.ChangeState(MultiplayerUserState.Ready);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Ready), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            await hub.ChangeSettings(testSettings);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Idle), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
        }

        [Fact]
        public async Task UserCantChangeSettingsWhenNotJoinedRoom()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task UserCantChangeSettingsWhenGameIsActive()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();

            using (var room = await hub.RoomStore.GetForUse(room_id))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task RoomSettingsUpdateNotifiesOtherUsers()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 1234567,
                BeatmapChecksum = "checksum",
                RulesetID = 2
            };

            await hub.JoinRoom(room_id);
            await hub.ChangeSettings(testSettings);
            mockReceiver.Verify(r => r.SettingsChanged(testSettings), Times.Once);
        }

        [Fact]
        public async Task ChangingSettingsToNonExistentBeatmapThrows()
        {
            mockDatabase.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync((string?)null);

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum",
            };

            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(testSettings));
        }

        [Fact]
        public async Task ChangingSettingsToCustomizedBeatmapThrows()
        {
            mockDatabase.Setup(d => d.GetBeatmapChecksumAsync(9999)).ReturnsAsync("correct checksum");

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 9999,
                BeatmapChecksum = "incorrect checksum",
            };

            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(testSettings));
        }

        [Theory]
        [InlineData(ILegacyRuleset.MAX_LEGACY_RULESET_ID + 1)]
        [InlineData(-1)]
        public async Task ChangingSettingsToCustomRulesetThrows(int rulesetID)
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetID,
            };

            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(testSettings));
        }

        #endregion

        #region Spectating

        [Fact]
        public async Task CanTransitionBetweenIdleAndSpectating()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Spectating);
            await hub.ChangeState(MultiplayerUserState.Idle);
        }

        [Fact]
        public async Task CanTransitionFromReadyToSpectating()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.ChangeState(MultiplayerUserState.Spectating);
        }

        [Fact]
        public async Task SpectatingUserStateDoesNotChange()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Spectating);

            setUserContext(mockContextUser1);

            await hub.StartMatch();
            mockGameplayReceiver.Verify(c => c.LoadRequested(), Times.Once);
            mockClients.Verify(clients => clients.Client(mockContextUser2.Object.ConnectionId).UserStateChanged(user_id_2, MultiplayerUserState.WaitingForLoad), Times.Never);

            await hub.ChangeState(MultiplayerUserState.Loaded);
            mockReceiver.Verify(c => c.MatchStarted(), Times.Once);
            mockClients.Verify(clients => clients.Client(mockContextUser2.Object.ConnectionId).UserStateChanged(user_id_2, MultiplayerUserState.Playing), Times.Never);

            await hub.ChangeState(MultiplayerUserState.FinishedPlay);
            mockReceiver.Verify(c => c.ResultsReady(), Times.Once);
            mockClients.Verify(clients => clients.Client(mockContextUser2.Object.ConnectionId).UserStateChanged(user_id_2, MultiplayerUserState.Results), Times.Never);
        }

        [Fact]
        public async Task SpectatingHostCanStartMatch()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Spectating);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser1);
            await hub.StartMatch();
            mockGameplayReceiver.Verify(c => c.LoadRequested(), Times.Once);
        }

        [Fact]
        public async Task SpectatingUserReceivesLoadRequestedAfterMatchStarted()
        {
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();
            mockReceiver.Verify(c => c.LoadRequested(), Times.Never);
            mockGameplayReceiver.Verify(c => c.LoadRequested(), Times.Once);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Spectating);
            mockCaller.Verify(c => c.LoadRequested(), Times.Once);

            // Ensure no other clients received LoadRequested().
            mockReceiver.Verify(c => c.LoadRequested(), Times.Never);
            mockGameplayReceiver.Verify(c => c.LoadRequested(), Times.Once);
        }

        #endregion

        private void verifyAddedToGameplayGroup(Mock<HubCallerContext> context, long roomId, bool wasAdded = true)
            => mockGroups.Verify(groups => groups.AddToGroupAsync(
                context.Object.ConnectionId,
                MultiplayerHub.GetGroupId(roomId, true),
                It.IsAny<CancellationToken>()), wasAdded ? Times.Once : Times.Never);

        private void verifyRemovedFromGameplayGroup(Mock<HubCallerContext> context, long roomId, bool wasRemoved = true)
            => mockGroups.Verify(groups => groups.RemoveFromGroupAsync(
                context.Object.ConnectionId,
                MultiplayerHub.GetGroupId(roomId, true),
                It.IsAny<CancellationToken>()), wasRemoved ? Times.Once : Times.Never);

        private void setUserContext(Mock<HubCallerContext> context) => hub.Context = context.Object;

        public class TestMultiplayerHub : MultiplayerHub
        {
            public EntityStore<MultiplayerRoom> RoomStore => ACTIVE_ROOMS;
            public EntityStore<MultiplayerClientState> UserStore => ACTIVE_STATES;

            public TestMultiplayerHub(MemoryDistributedCache cache, IDatabaseFactory databaseFactory)
                : base(cache, databaseFactory)
            {
            }

            public ItemUsage<MultiplayerRoom> GetRoom(long roomId) => RoomStore.GetForUse(roomId).Result;

            public bool CheckRoomExists(long roomId)
            {
                try
                {
                    using (var usage = RoomStore.GetForUse(roomId).Result)
                        return usage.Item != null;
                }
                catch
                {
                    // probably not tracked.
                    return false;
                }
            }
        }
    }
}
