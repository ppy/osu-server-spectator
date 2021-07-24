// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class RoomPlaylistTest : MultiplayerTest
    {
        [Fact]
        public async Task RoomHasNewPlaylistItemAfterMatchStart()
        {
            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            long expectedPlaylistItemId = playlistItemId + 1;

            Database.Setup(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()))
                    .ReturnsAsync(() => expectedPlaylistItemId);

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(expectedPlaylistItemId, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task ServerDoesNotAcceptClientPlaylistItemId()
        {
            await Hub.JoinRoom(ROOM_ID);

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                Name = "bestest room ever",
                BeatmapChecksum = "checksum",
                PlaylistItemId = 1
            };

            await Hub.ChangeSettings(testSettings);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(0, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }
    }
}
