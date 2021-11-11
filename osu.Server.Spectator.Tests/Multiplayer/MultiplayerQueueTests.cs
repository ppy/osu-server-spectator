// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task AddNonExistentBeatmap()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync((string?)null);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum"
            }));
        }

        [Fact]
        public async Task AddCustomizedBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(9999)).ReturnsAsync("correct checksum");

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 9999,
                BeatmapChecksum = "incorrect checksum",
            }));
        }

        [Theory]
        [InlineData(ILegacyRuleset.MAX_LEGACY_RULESET_ID + 1)]
        [InlineData(-1)]
        public async Task AddCustomRulesetThrows(int rulesetID)
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetID
            }));
        }

        [Fact]
        public async Task RoomStartsWithCurrentPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1, room.Settings.PlaylistItemId);
            }
        }

        [Fact]
        public async Task FairPlayPicksFromLeastPlayedUser()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");
            Database.Setup(d => d.GetBeatmapChecksumAsync(5555)).ReturnsAsync("5555");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FairRotate });

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            SetUserContext(ContextUser2);

            await Hub.JoinRoom(ROOM_ID);

            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 5555,
                BeatmapChecksum = "5555"
            });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 1 selected (user 1)
                Assert.Equal(1, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 4 selected (user 2)
                Assert.Equal(4, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 2 selected (user 1)
                Assert.Equal(2, room.Settings.PlaylistItemId);
            }

            await runGameplay();

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // Item 3 selected (user 1). User 2 has no more items available.
                Assert.Equal(3, room.Settings.PlaylistItemId);
            }

            async Task runGameplay()
            {
                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Ready);

                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.Ready);
                await Hub.StartMatch();
                await Hub.ChangeState(MultiplayerUserState.Loaded);

                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

                SetUserContext(ContextUser);
                await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
                await Hub.ChangeState(MultiplayerUserState.Idle);

                SetUserContext(ContextUser2);
                await Hub.ChangeState(MultiplayerUserState.Idle);

                SetUserContext(ContextUser);
            }
        }
    }
}
