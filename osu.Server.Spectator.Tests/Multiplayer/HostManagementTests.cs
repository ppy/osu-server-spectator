// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
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

            using (var user = await UserStates.GetForUse(USER_ID_2))
                Assert.Null(user.Item!.CurrentRoomID);
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
                Assert.True(room.Item?.Users.Any(u => u.UserID == USER_ID));
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
                Assert.True(room.Item?.Users.Any(u => u.UserID == USER_ID));
        }

        [Fact]
        public async Task HostAbortsMatchWhileMatchNotInProgress()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AbortMatch());

            UserReceiver.Verify(r => r.GameplayAborted(It.IsAny<GameplayAbortReason>()), Times.Never);
            User2Receiver.Verify(r => r.GameplayAborted(It.IsAny<GameplayAbortReason>()), Times.Never);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.All(u => u.State == MultiplayerUserState.Ready));
        }

        [Fact]
        public async Task HostAbortsInProgressMatch()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            // ensure both players actually transition to play (not stuck in waiting for load)
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            // Host exits and aborts.
            await Hub.AbortGameplay();
            await Hub.AbortMatch();

            UserReceiver.Verify(r => r.GameplayAborted(It.IsAny<GameplayAbortReason>()), Times.Once);
            User2Receiver.Verify(r => r.GameplayAborted(GameplayAbortReason.HostAbortedTheMatch), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.True(room.Item?.Users.All(u => u.State == MultiplayerUserState.Idle));

            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_aborted")), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_completed")), Times.Never);
        }

        [Fact]
        public async Task NonHostAbortsMatch()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            await LoadGameplay(ContextUser, ContextUser2);

            // User 2 attempts to abort the match.
            SetUserContext(ContextUser2);
            await Assert.ThrowsAsync<NotHostException>(() => Hub.AbortMatch());
        }
    }
}
