// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Queueing;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
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

                var expectedPlaylistItem = await Database.Object.GetCandidatePlaylistItemByExpiry(ROOM_ID);
                Assert.Equal(expectedPlaylistItem.id, room.Settings.PlaylistItemId);
            }
        }

        #region Host-Only Mode

        [Fact]
        public async Task GuestsCannotAddItemsInHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<NotHostException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));
        }

        [Fact]
        public async Task GuestsCannotRemoveItemsInHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<NotHostException>(() => Hub.RemovePlaylistItem(new APIPlaylistItem
            {
                ID = playlistItemId
            }));
        }

        [Fact]
        public async Task AddingItemUpdatesExistingInHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            var newItem = new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(playlistItemId, room.Settings.PlaylistItemId);
                Database.Verify(db => db.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Never);
                Receiver.Verify(r => r.PlaylistItemChanged(newItem), Times.Once);
            }
        }

        [Fact]
        public async Task RoomHasNewPlaylistItemAfterMatchStartInHostOnlyMode()
        {
            long firstItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            long secondItemId;

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var newItem = await room.QueueImplementation.GetCurrentItem(Database.Object);
                secondItemId = newItem.id;

                // Room has new playlist item.
                Assert.NotEqual(firstItemId, newItem.id);
                Assert.Equal(newItem.id, room.Settings.PlaylistItemId);
                Assert.False(newItem.expired);

                // Previous item is expired.
                Assert.True((await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, firstItemId))!.expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<APIPlaylistItem>(i => i.ID == newItem.id)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<APIPlaylistItem>(i => i.ID == firstItemId && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }

            // And a second time...
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var newItem = await room.QueueImplementation.GetCurrentItem(Database.Object);

                // Room has new playlist item.
                Assert.NotEqual(secondItemId, newItem.id);
                Assert.Equal(newItem.id, room.Settings.PlaylistItemId);

                // Previous item is expired.
                Assert.True((await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, secondItemId))!.expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.Is<APIPlaylistItem>(i => i.ID == newItem.id)), Times.Once);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<APIPlaylistItem>(i => i.ID == secondItemId && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task AddingItemDoesNotAffectPastItemsInHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long firstItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            var newItem = new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var firstItem = (await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, firstItemId))!;
                var currentItem = await room.QueueImplementation.GetCurrentItem(Database.Object);

                // Current item changed.
                Assert.Equal(newItem.BeatmapID, currentItem.beatmap_id);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<APIPlaylistItem>(i => i.ID == currentItem.id && i.BeatmapID == newItem.BeatmapID)), Times.Once);

                // Previous item unchanged.
                Assert.Equal(1234, firstItem.beatmap_id);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<APIPlaylistItem>(i => i.ID == firstItem.id && i.BeatmapID != 1234)), Times.Never);
            }
        }

        [Fact]
        public async Task ItemLastInQueueWhenChangingToFreeForAllMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // First item (played) still exists in the database.
                var firstItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 1);
                Assert.NotNull(firstItem);
                Assert.True(firstItem!.expired);

                // Second item (previously current) still exists and is the current item.
                var secondItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 2);
                Assert.NotNull(secondItem);
                Assert.False(secondItem!.expired);
                Assert.Equal(secondItem.id, room.Settings.PlaylistItemId);
            }
        }

        #endregion

        #region Free-For-All Mode

        [Fact]
        public async Task AddingItemAppendsToQueueInFreeForAllMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            var newItem = new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(playlistItemId, room.Settings.PlaylistItemId);
                Database.Verify(db => db.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Never);
                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Receiver.Verify(r => r.PlaylistItemAdded(newItem), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()), Times.Once);
            }
        }

        [Fact]
        public async Task CompletingItemExpiresAndDoesNotAddNewItemsInFreeForAllMode()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var currentItem = await room.QueueImplementation.GetCurrentItem(Database.Object);

                // Room maintains playlist item.
                Assert.Equal(currentItem.id, room.Settings.PlaylistItemId);
                Assert.True(currentItem.expired);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemAdded(It.IsAny<APIPlaylistItem>()), Times.Never);
                Receiver.Verify(r => r.PlaylistItemChanged(It.Is<APIPlaylistItem>(i => i.ID == currentItem.id && i.Expired)), Times.Once);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }

        [Fact]
        public async Task CanNotStartExpiredItemInFreeForAllMode()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task NewItemImmediatelySelectedWhenAllItemsExpiredInFreeForAllMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            var newItem = new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            };

            await Hub.AddPlaylistItem(newItem);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var currentItem = await room.QueueImplementation.GetCurrentItem(Database.Object);

                // New playlist item selected.
                Assert.Equal(newItem.BeatmapID, currentItem.beatmap_id);
                Assert.Equal(currentItem.id, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task AllNonExpiredItemsExceptCurrentRemovedWhenChangingToHostOnlyMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(d => d.GetBeatmapChecksumAsync(4444)).ReturnsAsync("4444");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.FreeForAll });

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

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Results);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueModes.HostOnly });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // First item (played) still exists in the database.
                var firstItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 1);
                Assert.NotNull(firstItem);
                Assert.True(firstItem!.expired);

                // Second item (previously current) still exists and is the current item.
                var secondItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 2);
                Assert.NotNull(secondItem);
                Assert.False(secondItem!.expired);
                Assert.Equal(secondItem.id, room.Settings.PlaylistItemId);

                // Third item (future item) removed.
                var thirdItem = await Database.Object.GetPlaylistItemFromRoomAsync(ROOM_ID, 3);
                Assert.Null(thirdItem);

                // Players received callbacks.
                Receiver.Verify(r => r.PlaylistItemRemoved(It.Is<APIPlaylistItem>(p => p.ID == 3)), Times.Once);
            }
        }

        #endregion
    }
}
