// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class RoomSettingsTests : MultiplayerTest
    {
        [Fact]
        public async Task ChangingSettingsUpdatesModel()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                MatchType = MatchType.HeadToHead
            };

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(testSettings);

            using (var usage = await Hub.GetRoom(ROOM_ID))
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
                MatchType = MatchType.HeadToHead
            };

            await Hub.JoinRoom(ROOM_ID);

            MultiplayerRoom? room;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                // unsafe, but just for tests.
                room = usage.Item;
                Debug.Assert(room != null);
            }

            await Hub.ChangeState(MultiplayerUserState.Ready);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Ready), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Ready, u.State));

            await Hub.ChangeSettings(testSettings);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Idle), Times.Once);
            Assert.All(room.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));
        }

        [Fact]
        public async Task UserCantChangeSettingsWhenNotJoinedRoom()
        {
            await Assert.ThrowsAsync<NotJoinedRoomException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task UserCantChangeSettingsWhenGameIsActive()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings()));
        }

        [Fact]
        public async Task RoomSettingsUpdateNotifiesOtherUsers()
        {
            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Password = "password",
                MatchType = MatchType.HeadToHead,
            };

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(testSettings);
            Receiver.Verify(r => r.SettingsChanged(testSettings), Times.Once);
        }

        [Fact]
        public async Task ChangingSettingsToUnsupportedMatchTypeThrows()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                MatchType = MatchType.Playlists,
            }));
        }

        [Fact]
        public async Task ServerDoesNotAcceptClientPlaylistItemId()
        {
            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                PlaylistItemId = 1
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(playlistItemId, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task ChangingQueueModeUpdatesModel()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                QueueMode = QueueMode.AllPlayers
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;

                Debug.Assert(room != null);
                Assert.Equal(QueueMode.AllPlayers, room.Settings.QueueMode);
            }
        }
    }
}
