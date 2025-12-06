// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerFlowTests : MultiplayerTest
    {
        /// <summary>
        /// Tests a full game flow with one user in the room.
        /// </summary>
        [Fact]
        public async Task SingleUserMatchFlow()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
            }

            // some users enter a ready state.
            await MarkCurrentUserReadyAndAvailable();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            // host requests the start of the match.
            await Hub.StartMatch();

            // server requests the all users start loading.
            Receiver.Verify(r => r.LoadRequested(), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.WaitingForLoad), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            }

            // all users finish loading.
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Playing), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
            }

            // server requests users start playing.
            UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
            }

            // all users finish playing.
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            // server lets players know that results are ready for consumption (all players have finished).
            Receiver.Verify(r => r.ResultsReady(), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Results), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));
            }

            // players return back to idle state as they please.
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
            }

            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_completed")), Times.Once);
        }

        /// <summary>
        /// Tests a full game flow with two users in the room.
        /// Focuses on the interactions during loading sections.
        /// </summary>
        [Fact]
        public async Task MultiUserMatchFlow()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
            }

            // both users become ready.
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await MarkCurrentUserReadyAndAvailable();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));
            }

            // host requests the start of the match.
            SetUserContext(ContextUser);
            await Hub.StartMatch();

            // server requests the all users start loading.
            Receiver.Verify(r => r.LoadRequested(), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            }

            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.WaitingForLoad), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.WaitingForLoad), Times.Once);

            // first user finishes loading.
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            // room is still waiting for second user to load.
            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
            }

            Receiver.Verify(r => r.GameplayStarted(), Times.Never);

            // second user finishes loading, which triggers gameplay to start.
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);

                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
                UserReceiver.Verify(r => r.GameplayStarted(), Times.Once);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Playing), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Playing), Times.Once);
            }

            // first user finishes playing.
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);

                // room is still waiting for second user to finish playing.
                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.FinishedPlay), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Playing), Times.Once);
                Receiver.Verify(r => r.ResultsReady(), Times.Never);
            }

            // second user finishes playing.
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                // server lets players know that results are ready for consumption (all players have finished).
                Receiver.Verify(r => r.ResultsReady(), Times.Once);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Results), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Results), Times.Once);

                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }
        }

        [Fact]
        public async Task SecondUserDoesReceiveLoadRequestWhenMatchRestartedAndNotReady()
        {
            // Start the match initially with both users entering gameplay.
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            // Finish gameplay for both users.
            SetUserContext(ContextUser2);
            await Hub.AbortGameplay();
            SetUserContext(ContextUser);
            await Hub.AbortGameplay();

            // Restart gameplay with just host being ready.
            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            // Both Host and second user receive it twice.
            UserReceiver.Verify(r => r.LoadRequested(), Times.Exactly(2));
            User2Receiver.Verify(r => r.LoadRequested(), Times.Exactly(2));
        }

        [Fact]
        public async Task CanNotVoteToSkipOutsideOfGameplay()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.VoteToSkipIntro());
        }

        [Fact]
        public async Task VoteToSkip()
        {
            CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);

            // Join all users.
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            // Start gameplay
            SetUserContext(ContextUser);
            await Hub.StartMatch();
            await LoadGameplay(ContextUser, ContextUser2, contextUser3);

            // User 1 skips
            SetUserContext(ContextUser);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

            // User 1 skips again (should be a no-op)
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

            // User 2 skips
            SetUserContext(ContextUser2);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID_2, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);

            // User 3 skips (additional messages should be a no-op for clients)
            SetUserContext(contextUser3);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(3, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Exactly(2));
        }

        [Fact]
        public async Task VoteToSkipUpdatesOnLeave()
        {
            CreateUser(3, out Mock<HubCallerContext> contextUser3, out _);
            CreateUser(4, out Mock<HubCallerContext> contextUser4, out _);

            // Join all users.
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(contextUser4);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            // Start gameplay
            SetUserContext(ContextUser);
            await Hub.StartMatch();
            await LoadGameplay(ContextUser, ContextUser2, contextUser3, contextUser4);

            // User 1 skips
            SetUserContext(ContextUser);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

            // User 2 skips
            SetUserContext(ContextUser2);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID_2, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

            // User 3 leaves
            SetUserContext(contextUser3);
            await Hub.LeaveRoom();
            Receiver.Verify(r => r.UserVotedToSkipIntro(3, It.IsAny<bool>()), Times.Never);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);
        }

        [Fact]
        public async Task VoteToSkipResetOnGameplayState()
        {
            // Join all users.
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            // Start gameplay
            SetUserContext(ContextUser);
            await Hub.StartMatch();
            await LoadGameplay(ContextUser, ContextUser2);

            // User 1 skips
            SetUserContext(ContextUser);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Never);

            // User 2 skips
            SetUserContext(ContextUser2);
            await Hub.VoteToSkipIntro();
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID_2, true), Times.Once);
            Receiver.Verify(r => r.VoteToSkipIntroPassed(), Times.Once);

            // Start new gameplay session
            SetUserContext(ContextUser);
            await Hub.AbortMatch();
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadGameplay(ContextUser, ContextUser2);

            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID, false), Times.Once);
            Receiver.Verify(r => r.UserVotedToSkipIntro(USER_ID_2, false), Times.Once);
        }
    }
}
