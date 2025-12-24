// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using System.Linq;
using Moq;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class UserStateManagementTests : MultiplayerTest
    {
        [Fact]
        public async Task UserStateChangeNotifiesOtherUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Ready), Times.Once);
        }

        [Theory]
        [InlineData(MultiplayerUserState.WaitingForLoad)]
        [InlineData(MultiplayerUserState.Playing)]
        [InlineData(MultiplayerUserState.Results)]
        public async Task UserCantChangeStateToReservedStates(MultiplayerUserState reservedState)
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateChangeException>(() => Hub.ChangeState(reservedState));
        }

        [Fact]
        public async Task StartingMatchWithNoReadyUsersFails()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task StartingMatchWithHostNotReadyFails()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task StartingAlreadyStartedMatchFails()
        {
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task AllUsersBackingOutFromLoadCancelsTransitionToPlay()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
            }

            await Hub.AbortGameplay();
            SetUserContext(ContextUser2);
            await Hub.AbortGameplay();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }
        }

        [Fact]
        public async Task AllUsersAbortingGameplayIndividuallyLogsGameAsAbortedAndExpiresPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            long playlistItemId;

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);

                playlistItemId = room.Item.CurrentPlaylistItem.ID;
            }

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            SetUserContext(ContextUser);
            await Hub.AbortGameplay();
            SetUserContext(ContextUser2);
            await Hub.AbortGameplay();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            Database.Verify(db => db.MarkPlaylistItemAsPlayedAsync(ROOM_ID, It.IsAny<long>()), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(
                It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_aborted" && ev.playlist_item_id == playlistItemId)), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(
                It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_completed")), Times.Never);
        }

        [Fact]
        public async Task HostAbortingGameplayBeforeStartLogsGameAsAbortedAndExpiresPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            long playlistItemId;

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);

                playlistItemId = room.Item.CurrentPlaylistItem.ID;
            }

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            SetUserContext(ContextUser);
            await Hub.AbortMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            Database.Verify(db => db.MarkPlaylistItemAsPlayedAsync(ROOM_ID, It.IsAny<long>()), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(
                It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_started" && ev.playlist_item_id == playlistItemId)), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(
                It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_aborted" && ev.playlist_item_id == playlistItemId)), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(
                It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_completed")), Times.Never);
        }

        [Fact]
        public async Task HostAbortingGameplayAfterStartLogsGameAsAbortedAndExpiresPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
            }

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
                Assert.Collection(room.Item.Users.Select(u => u.State).ToArray(),
                    state => Assert.Equal(MultiplayerUserState.Playing, state),
                    state => Assert.Equal(MultiplayerUserState.Playing, state));
            }

            await Hub.AbortMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            Database.Verify(db => db.MarkPlaylistItemAsPlayedAsync(ROOM_ID, It.IsAny<long>()), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_started")), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_aborted")), Times.Once);
            Database.Verify(db => db.LogRoomEventAsync(It.Is<multiplayer_realtime_room_event>(ev => ev.event_type == "game_completed")), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(DownloadState.Unknown)]
        [InlineData(DownloadState.Downloading)]
        [InlineData(DownloadState.NotDownloaded)]
        public async Task UsersWithoutBeatmapWillNotEnterGameplay(DownloadState? state)
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            // user 2 is not ready and doesn't have the beatmap.
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            if (state.HasValue)
                await Hub.ChangeBeatmapAvailability(new BeatmapAvailability(state.Value));

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);

                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);

                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.WaitingForLoad);
                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.Idle);
            }

            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.Playing);
                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.Idle);
            }
        }

        [Fact]
        public async Task BothReadyAndIdleUsersTransitionToPlay()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            // user 2 is not ready but has the beatmap. should join gameplay.
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);

                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
                Assert.Equal(2, room.Item.Users.Count(u => u.State == MultiplayerUserState.WaitingForLoad));
            }

            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(2, room.Item.Users.Count(u => u.State == MultiplayerUserState.Playing));
            }
        }

        [Fact]
        public async Task UserDisconnectsDuringGameplayUpdatesRoomState()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item.State);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            }

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
            }

            // first user exits gameplay
            SetUserContext(ContextUser);
            await Hub.AbortGameplay();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Playing, room.Item.State);
            }

            // second user gets disconnected
            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }
        }

        [Fact]
        public async Task OnlyFinishedUsersTransitionToResults()
        {
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            SetUserContext(ContextUser);

            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser, ContextUser2);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(2, room.Item.Users.Count(u => u.State == MultiplayerUserState.Results));
            }
        }

        [Fact]
        public async Task IdleUsersDoGetLoadRequest()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());

            SetUserContext(ContextUser);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.All(room.Item.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
            }

            // one user enters a ready state.
            await MarkCurrentUserReadyAndAvailable();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.Idle);
                Assert.Single(room.Item.Users, u => u.State == MultiplayerUserState.Ready);

                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            // host requests the start of the match.
            await Hub.StartMatch();

            UserReceiver.Verify(r => r.LoadRequested(), Times.Once);
            User2Receiver.Verify(r => r.LoadRequested(), Times.Once);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.True(room.Item.Users.All(u => u.State == MultiplayerUserState.WaitingForLoad));
            }
        }

        [Fact]
        public async Task UserCanNotSwitchToIdleDuringGameplay()
        {
            await Hub.JoinRoom(ROOM_ID);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            // Test during WaitingForLoad state.
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerUserState.WaitingForLoad, room.Item.Users[0].State);
            }

            // Test during Playing state.
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerUserState.Playing, room.Item.Users[0].State);
            }

            // Test during FinishedPlay state (allows switching to idle).
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerUserState.Idle, room.Item.Users[0].State);
            }
        }

        [Fact]
        public async Task UserSwitchedToIdleWhenAbortingGameplay()
        {
            await Hub.JoinRoom(ROOM_ID);

            // Test during WaitingForLoad state.
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await Hub.AbortGameplay();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerUserState.Idle, room.Item.Users[0].State);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }

            // Test during Playing state.
            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.AbortGameplay();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.NotNull(room.Item);
                Assert.Equal(MultiplayerUserState.Idle, room.Item.Users[0].State);
                Assert.Equal(MultiplayerRoomState.Open, room.Item.State);
            }
        }

        [Fact]
        public async Task CanNotAbortGameplayInNonGameplayStates()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AbortGameplay());

            await MarkCurrentUserReadyAndAvailable();
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AbortGameplay());
        }

        [Fact]
        public async Task KickUser()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Hub.KickUser(USER_ID_2);

            UserReceiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Once);
            User2Receiver.Verify(r => r.UserKicked(It.IsAny<MultiplayerRoomUser>()), Times.Once);
            UserReceiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Never);
            User2Receiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Never);
        }

        [Fact]
        public async Task LeaveOnDisconnect()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Hub.OnDisconnectedAsync(null);

            Receiver.Verify(r => r.UserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Once);
        }
    }
}
