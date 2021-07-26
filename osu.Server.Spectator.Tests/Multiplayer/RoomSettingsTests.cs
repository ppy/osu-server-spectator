// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Rulesets;
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
                BeatmapChecksum = "checksum"
            };

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(testSettings);

            using (var usage = Hub.GetRoom(ROOM_ID))
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

            await Hub.JoinRoom(ROOM_ID);

            MultiplayerRoom? room;

            using (var usage = Hub.GetRoom(ROOM_ID))
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

            using (var room = await Hub.ActiveRooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings()));
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

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(testSettings);
            Receiver.Verify(r => r.SettingsChanged(testSettings), Times.Once);
        }

        [Fact]
        public async Task ChangingSettingsToNonExistentBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync((string?)null);

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum",
            };

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(testSettings));
        }

        [Fact]
        public async Task ChangingSettingsToCustomizedBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(9999)).ReturnsAsync("correct checksum");

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 9999,
                BeatmapChecksum = "incorrect checksum",
            };

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(testSettings));
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

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(testSettings));
        }
    }
}
