// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MatchSpectatingTests : MultiplayerTest
    {
        [Fact]
        public async Task CanTransitionBetweenIdleAndSpectating()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Spectating);
            await Hub.ChangeState(MultiplayerUserState.Idle);
        }

        [Fact]
        public async Task CanTransitionFromReadyToSpectating()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.ChangeState(MultiplayerUserState.Spectating);
        }

        [Fact]
        public async Task SpectatingUserStateDoesNotChange()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Spectating);

            SetUserContext(ContextUser);

            await Hub.StartMatch();
            UserReceiver.Verify(c => c.LoadRequested(), Times.Once);
            Clients.Verify(clients => clients.Client(ContextUser2.Object.ConnectionId).UserStateChanged(USER_ID_2, MultiplayerUserState.WaitingForLoad), Times.Never);

            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            UserReceiver.Verify(c => c.GameplayStarted(), Times.Once);
            Clients.Verify(clients => clients.Client(ContextUser2.Object.ConnectionId).UserStateChanged(USER_ID_2, MultiplayerUserState.Playing), Times.Never);

            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            Receiver.Verify(c => c.ResultsReady(), Times.Once);
            Clients.Verify(clients => clients.Client(ContextUser2.Object.ConnectionId).UserStateChanged(USER_ID_2, MultiplayerUserState.Results), Times.Never);
        }

        [Fact]
        public async Task SpectatingHostCanStartMatch()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Spectating);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Hub.StartMatch();
            UserReceiver.Verify(c => c.LoadRequested(), Times.Once);
        }

        [Fact]
        public async Task SpectatingUserReceivesLoadRequestedAfterGameplayStarted()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            UserReceiver.Verify(c => c.LoadRequested(), Times.Once);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Spectating);
            Caller.Verify(c => c.LoadRequested(), Times.Once);

            // Ensure no other clients received LoadRequested().
            UserReceiver.Verify(c => c.LoadRequested(), Times.Once);
            User2Receiver.Verify(c => c.LoadRequested(), Times.Never);
        }
    }
}
