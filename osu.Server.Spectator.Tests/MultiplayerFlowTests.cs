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
using osu.Game.Online.RealtimeMultiplayer;
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

        private readonly Mock<IMultiplayerClient> mockReceiver;
        private readonly Mock<IMultiplayerClient> mockGameplayReceiver;

        private readonly Mock<HubCallerContext> mockContextUser1;
        private readonly Mock<HubCallerContext> mockContextUser2;

        private readonly Mock<IGroupManager> mockGroups;

        public MultiplayerFlowTests()
        {
            MultiplayerHub.Reset();

            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            hub = new TestMultiplayerHub(cache);

            hub.RoomHostId = user_id;

            mockGroups = new Mock<IGroupManager>();

            mockContextUser1 = new Mock<HubCallerContext>();
            mockContextUser1.Setup(context => context.UserIdentifier).Returns(user_id.ToString());
            mockContextUser1.Setup(context => context.ConnectionId).Returns(user_id.ToString());

            mockContextUser2 = new Mock<HubCallerContext>();
            mockContextUser2.Setup(context => context.UserIdentifier).Returns(user_id_2.ToString());
            mockContextUser2.Setup(context => context.ConnectionId).Returns(user_id_2.ToString());

            Mock<IHubCallerClients<IMultiplayerClient>> mockClients = new Mock<IHubCallerClients<IMultiplayerClient>>();

            mockReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id, false))).Returns(mockReceiver.Object);

            mockGameplayReceiver = new Mock<IMultiplayerClient>();
            mockClients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(room_id, true))).Returns(mockGameplayReceiver.Object);

            hub.Groups = mockGroups.Object;
            hub.Clients = mockClients.Object;

            setUserContext(mockContextUser1);
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
            var room = await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            Assert.Equal(room, await hub.JoinRoom(room_id));

            setUserContext(mockContextUser1);
            await hub.TransferHost(user_id_2);

            mockReceiver.Verify(r => r.HostChanged(user_id_2), Times.Once);
            Assert.True(room.Host?.UserID == user_id_2);
        }

        [Fact]
        public async Task HostLeavingCausesHostTransfer()
        {
            setUserContext(mockContextUser1);
            var room = await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.LeaveRoom();

            mockReceiver.Verify(r => r.HostChanged(user_id_2), Times.Once);
            Assert.True(room.Host?.UserID == user_id_2);
        }

        #endregion

        #region Joining and leaving

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

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.LeaveRoom();

            // room still exists even though the original host left
            Assert.True(hub.CheckRoomExists(room_id));

            setUserContext(mockContextUser2);
            await hub.LeaveRoom();

            // room is gone.
            Assert.False(hub.CheckRoomExists(room_id));
        }

        [Fact]
        public async Task UserCantLeaveWhenNotAlreadyJoined()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => hub.LeaveRoom());
        }

        [Fact]
        public async Task UserJoinLeaveNotifiesOtherUsers()
        {
            await hub.JoinRoom(room_id); // join an arbitrary first user (listener).

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.JoinRoom(room_id)); // invalid join

            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id_2)), Times.Once);

            await hub.LeaveRoom();
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id_2)), Times.Once);

            await hub.JoinRoom(room_id);
            mockReceiver.Verify(r => r.UserJoined(new MultiplayerRoomUser(user_id_2)), Times.Exactly(2));

            await hub.LeaveRoom();
            mockReceiver.Verify(r => r.UserLeft(new MultiplayerRoomUser(user_id_2)), Times.Exactly(2));
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
            hub.MarkRoomActiveShouldThrow = true;
            await Assert.ThrowsAnyAsync<Exception>(() => hub.JoinRoom(room_id));

            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.RoomStore.GetForUse(room_id));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task UserJoinPostJoinFailureCleansUpRoomAndUser()
        {
            hub.UpdateDatabaseParticipantsShouldThrow = true;
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
            var room = await hub.JoinRoom(room_id);

            await hub.ChangeState(MultiplayerUserState.Ready);

            Assert.Equal(MultiplayerRoomState.Open, room.State);

            await hub.StartMatch();

            Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => hub.StartMatch());
        }

        [Fact]
        public async Task AllUsersBackingOutFromLoadCancelsTransitionToPlay()
        {
            var room = await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();

            Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.State);

            await hub.ChangeState(MultiplayerUserState.Idle);
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Idle);

            Assert.Equal(MultiplayerRoomState.Open, room.State);
        }

        [Fact]
        public async Task OnlyReadiedUpUsersTransitionToPlay()
        {
            var room = await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);
            await hub.StartMatch();
            Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.State);
            Assert.Single(room.Users, u => u.State == MultiplayerUserState.WaitingForLoad);
            Assert.Single(room.Users, u => u.State == MultiplayerUserState.Idle);

            await hub.ChangeState(MultiplayerUserState.Loaded);
            Assert.Single(room.Users, u => u.State == MultiplayerUserState.Playing);
            Assert.Single(room.Users, u => u.State == MultiplayerUserState.Idle);
        }

        [Fact]
        public async Task OnlyFinishedUsersTransitionToResults()
        {
            var room = await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);

            await hub.StartMatch();
            await hub.ChangeState(MultiplayerUserState.Loaded);

            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            verifyRemovedFromGameplayGroup(mockContextUser1, room_id);
            verifyRemovedFromGameplayGroup(mockContextUser2, room_id, false);

            Assert.Single(room.Users, u => u.State == MultiplayerUserState.Results);
            Assert.Single(room.Users, u => u.State == MultiplayerUserState.Idle);
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
            var room = await hub.JoinRoom(room_id);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // some users enter a ready state.
            await hub.ChangeState(MultiplayerUserState.Ready);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            Assert.Equal(MultiplayerRoomState.Open, room.State);

            // host requests the start of the match.
            await hub.StartMatch();

            // server requests the all users start loading.
            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.WaitingForLoad), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));

            // all users finish loading.
            await hub.ChangeState(MultiplayerUserState.Loaded);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Playing), Times.Once);
            Assert.Equal(MultiplayerRoomState.Playing, room.State);

            // server requests users start playing.
            mockReceiver.Verify(r => r.MatchStarted(), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));

            // all users finish playing.
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);
            Assert.Equal(MultiplayerRoomState.Open, room.State);

            // server lets players know that results are ready for consumption (all players have finished).
            mockReceiver.Verify(r => r.ResultsReady(), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Results), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));

            // players return back to idle state as they please.
            await hub.ChangeState(MultiplayerUserState.Idle);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
        }

        /// <summary>
        /// Tests a full game flow with two users in the room.
        /// Focuses on the interactions during loading sections.
        /// </summary>
        [Fact]
        public async Task MultiUserMatchFlow()
        {
            var room = await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // both users become ready.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Ready);
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Ready);

            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            // host requests the start of the match.
            setUserContext(mockContextUser1);
            await hub.StartMatch();

            // server requests the all users start loading.
            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.WaitingForLoad), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.WaitingForLoad), Times.Once);

            // first user finishes loading.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.Loaded);

            // room is still waiting for second user to load.
            Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.State);
            mockReceiver.Verify(r => r.MatchStarted(), Times.Never);

            // second user finishes loading, which triggers gameplay to start.
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.Loaded);

            Assert.Equal(MultiplayerRoomState.Playing, room.State);
            mockReceiver.Verify(r => r.MatchStarted(), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Playing), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Playing), Times.Once);

            // first user finishes playing.
            setUserContext(mockContextUser1);
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            // room is still waiting for second user to finish playing.
            Assert.Equal(MultiplayerRoomState.Playing, room.State);
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.FinishedPlay), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Playing), Times.Once);
            mockReceiver.Verify(r => r.ResultsReady(), Times.Never);

            // second user finishes playing.
            setUserContext(mockContextUser2);
            await hub.ChangeState(MultiplayerUserState.FinishedPlay);

            // server lets players know that results are ready for consumption (all players have finished).
            mockReceiver.Verify(r => r.ResultsReady(), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));
            mockReceiver.Verify(r => r.UserStateChanged(user_id, MultiplayerUserState.Results), Times.Once);
            mockReceiver.Verify(r => r.UserStateChanged(user_id_2, MultiplayerUserState.Results), Times.Once);

            Assert.Equal(MultiplayerRoomState.Open, room.State);
        }

        [Fact]
        public async void NotReadyUsersDontGetLoadRequest()
        {
            var room = await hub.JoinRoom(room_id);

            setUserContext(mockContextUser2);
            await hub.JoinRoom(room_id);

            setUserContext(mockContextUser1);

            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // one user enters a ready state.
            await hub.ChangeState(MultiplayerUserState.Ready);

            Assert.Single(room.Users.Where(u => u.State == MultiplayerUserState.Idle));
            Assert.Single(room.Users.Where(u => u.State == MultiplayerUserState.Ready));

            Assert.Equal(MultiplayerRoomState.Open, room.State);

            // host requests the start of the match.
            await hub.StartMatch();

            mockGameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            mockReceiver.Verify(r => r.LoadRequested(), Times.Never);

            Assert.Single(room.Users.Where(u => u.State == MultiplayerUserState.WaitingForLoad));
            Assert.Single(room.Users.Where(u => u.State == MultiplayerUserState.Idle));
        }

        #endregion

        #region Room Settings

        [Fact]
        public async Task ChangingSettingsUpdatesModel()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
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
            var room = await hub.JoinRoom(room_id);
            await hub.ChangeState(MultiplayerUserState.Ready);
            await hub.StartMatch();
            Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.State);
            await Assert.ThrowsAsync<InvalidStateException>(() => hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task RoomSettingsUpdateNotifiesOtherUsers()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 1234567,
                RulesetID = 2
            };

            await hub.JoinRoom(room_id);
            await hub.ChangeSettings(testSettings);
            mockReceiver.Verify(r => r.SettingsChanged(testSettings), Times.Once);
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

            public TestMultiplayerHub(MemoryDistributedCache cache)
                : base(cache)
            {
            }

            public bool ClearDatabaseScoresShouldThrow;
            protected override Task ClearDatabaseScores(MultiplayerRoom room) => ClearDatabaseScoresShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool UpdateDatabaseParticipantsShouldThrow;
            protected override Task UpdateDatabaseParticipants(MultiplayerRoom room) => UpdateDatabaseParticipantsShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool UpdateDatabaseSettingsShouldThrow;
            protected override Task UpdateDatabaseSettings(MultiplayerRoom room) => UpdateDatabaseSettingsShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool UpdateDatabaseHostShouldThrow;
            protected override Task UpdateDatabaseHost(MultiplayerRoom room) => UpdateDatabaseHostShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool EndDatabaseMatchShouldThrow;
            protected override Task EndDatabaseMatch(MultiplayerRoom room) => EndDatabaseMatchShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool MarkRoomActiveShouldThrow;
            protected override Task MarkRoomActive(MultiplayerRoom room) => MarkRoomActiveShouldThrow ? throw new InvalidOperationException() : Task.CompletedTask;

            public bool CheckIsUserRestrictedShouldThrow;
            protected virtual Task<bool> CheckIsUserRestricted() => CheckIsUserRestrictedShouldThrow ? throw new InvalidOperationException() : Task.FromResult(false);

            public int RoomHostId;

            protected override Task<MultiplayerRoom> RetrieveRoom(long roomId)
            {
                if (RoomHostId != CurrentContextUserId)
                    throw new InvalidStateException("Non-host is attempting to join match before host");

                // bypass database for testing.
                return Task.FromResult(new MultiplayerRoom(roomId)
                {
                    Host = new MultiplayerRoomUser(RoomHostId)
                });
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
