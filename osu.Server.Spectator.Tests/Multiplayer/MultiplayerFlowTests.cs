// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
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
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // some users enter a ready state.
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
            {
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }

            // host requests the start of the match.
            await Hub.StartMatch();

            // server requests the all users start loading.
            GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.WaitingForLoad), Times.Once);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))

                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));

            // all users finish loading.
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Playing), Times.Once);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);

            // server requests users start playing.
            Receiver.Verify(r => r.MatchStarted(), Times.Once);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));

            // all users finish playing.
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);

            // server lets players know that results are ready for consumption (all players have finished).
            Receiver.Verify(r => r.ResultsReady(), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Results), Times.Once);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));

            // players return back to idle state as they please.
            await Hub.ChangeState(MultiplayerUserState.Idle);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
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

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // both users become ready.
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            // host requests the start of the match.
            SetUserContext(ContextUser);
            await Hub.StartMatch();

            // server requests the all users start loading.
            GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.WaitingForLoad), Times.Once);
            Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.WaitingForLoad), Times.Once);

            // first user finishes loading.
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            // room is still waiting for second user to load.
            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);
            Receiver.Verify(r => r.MatchStarted(), Times.Never);

            // second user finishes loading, which triggers gameplay to start.
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
            {
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
                Receiver.Verify(r => r.MatchStarted(), Times.Once);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Playing), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Playing), Times.Once);
            }

            // first user finishes playing.
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
            {
                // room is still waiting for second user to finish playing.
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.FinishedPlay), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Playing), Times.Once);
                Receiver.Verify(r => r.ResultsReady(), Times.Never);
            }

            // second user finishes playing.
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var room = await Hub.Rooms.GetForUse(ROOM_ID))
            {
                // server lets players know that results are ready for consumption (all players have finished).
                Receiver.Verify(r => r.ResultsReady(), Times.Once);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Results, u.State));
                Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Results), Times.Once);
                Receiver.Verify(r => r.UserStateChanged(USER_ID_2, MultiplayerUserState.Results), Times.Once);

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }
        }
    }
}
