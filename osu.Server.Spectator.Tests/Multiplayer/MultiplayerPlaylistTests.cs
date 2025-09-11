// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Standard;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerPlaylistTests : MultiplayerTest
    {
        [Fact]
        public async Task AddNonExistentBeatmap()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync((database_beatmap?)null);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum"
            }));
        }

        [Fact]
        public async Task AddCustomizedBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapAsync(9999)).ReturnsAsync(new database_beatmap { checksum = "correct checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
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
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetID
            }));
        }

        [Fact]
        public async Task AddingItemWithRulesetIncompatibleWithBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapAsync(9999)).ReturnsAsync(new database_beatmap
            {
                checksum = "checksum",
                playmode = 2,
            });

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 9999,
                BeatmapChecksum = "checksum",
                RulesetID = 3,
            }));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(0, 3)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        public async Task AddingItemWithRulesetCompatibleWithBeatmapSucceeds(ushort beatmapRulesetId, ushort scoreRulesetId)
        {
            Database.Setup(d => d.GetBeatmapAsync(9999)).ReturnsAsync(new database_beatmap
            {
                checksum = "checksum",
                playmode = beatmapRulesetId,
            });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 9999,
                BeatmapChecksum = "checksum",
                RulesetID = scoreRulesetId,
            });

            Database.Verify(d => d.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
        }

        [Fact]
        public async Task RoomStartsWithCurrentPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1, room.Settings.PlaylistItemId);
            }
        }

        [Fact]
        public async Task RoomStartsWithCorrectQueueingMode()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID,
                        queue_mode = database_queue_mode.all_players
                    });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);
            }
        }

        [Fact]
        public async Task JoinedRoomContainsAllPlaylistItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            InitialiseRoom(ROOM_ID);

            await Database.Object.AddPlaylistItemAsync(new multiplayer_playlist_item(ROOM_ID, new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));

            await Hub.JoinRoom(ROOM_ID);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);
                Assert.Equal(1234, room.Playlist[0].BeatmapID);
                Assert.Equal(3333, room.Playlist[1].BeatmapID);
            }
        }

        [Fact]
        public async Task GuestsCanRemoveTheirOwnItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.RemovePlaylistItem(2);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist);
                Database.Verify(db => db.RemovePlaylistItemAsync(ROOM_ID, 2), Times.Once);
                Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
            }
        }

        [Fact]
        public async Task GuestsCanNotRemoveOtherUsersItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser2);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(2));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task HostCanRemoveGuestsItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser);
            await Hub.RemovePlaylistItem(2);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist);
                Database.Verify(db => db.RemovePlaylistItemAsync(ROOM_ID, 2), Times.Once);
                Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
            }
        }

        [Fact]
        public async Task ExternalItemsCanNotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(3));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task CurrentItemCanNotBeRemovedIfSingle()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));
        }

        [Fact]
        public async Task CurrentItemCanBeRemovedIfNotSingle()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.RemovePlaylistItem(1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Single(room.Playlist);
                Assert.Equal(2, room.Settings.PlaylistItemId);
                Database.Verify(db => db.RemovePlaylistItemAsync(ROOM_ID, 1), Times.Once);
                Receiver.Verify(client => client.PlaylistItemRemoved(1), Times.Once);
            }
        }

        [Fact]
        public async Task ExpiredItemsCanNotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task GuestsCanUpdateTheirOwnItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap { checksum = "4444" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 2,
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);

                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Receiver.Verify(client => client.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);

                Database.Verify(db => db.UpdatePlaylistItemAsync(It.Is<multiplayer_playlist_item>(i => i.id == 2 && i.beatmap_id == 4444)), Times.Once);
                Receiver.Verify(client => client.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == 2 && i.BeatmapID == 4444)), Times.Once);
            }
        }

        [Fact]
        public async Task EditingItemWithRulesetIncompatibleWithBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap
            {
                checksum = "3333",
                playmode = 1,
            });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap
            {
                checksum = "4444",
                playmode = 3,
            });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
                RulesetID = 1,
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 2,
                BeatmapID = 4444,
                BeatmapChecksum = "4444",
                RulesetID = 1,
            }));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(0, 3)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        public async Task EditingItemWithRulesetCompatibleWithBeatmapSucceeds(ushort beatmapRulesetId, ushort scoreRulesetId)
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333", });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap
            {
                checksum = "4444",
                playmode = beatmapRulesetId,
            });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333",
            });

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 2,
                BeatmapID = 4444,
                BeatmapChecksum = "4444",
                RulesetID = scoreRulesetId,
            });
            Database.Verify(d => d.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task HostCanUpdateGuestsItems()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap { checksum = "4444" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 2,
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);

                Database.Verify(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()), Times.Once);
                Receiver.Verify(client => client.PlaylistItemAdded(It.IsAny<MultiplayerPlaylistItem>()), Times.Once);

                Database.Verify(db => db.UpdatePlaylistItemAsync(It.Is<multiplayer_playlist_item>(i => i.id == 2 && i.beatmap_id == 4444)), Times.Once);
                Receiver.Verify(client => client.PlaylistItemChanged(It.Is<MultiplayerPlaylistItem>(i => i.ID == 2 && i.BeatmapID == 4444)), Times.Once);
            }
        }

        [Fact]
        public async Task HostCanChangeItemsWhenMaxItemsReached()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap { checksum = "4444" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            for (int i = 1; i < StandardMatchController.GUEST_PLAYLIST_LIMIT; i++)
            {
                await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
                {
                    BeatmapID = 3333,
                    BeatmapChecksum = "3333"
                });
            }

            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.HostOnly });
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(StandardMatchController.GUEST_PLAYLIST_LIMIT, room.Playlist.Count);
                Assert.Equal(4444, room.Playlist[0].BeatmapID);
            }
        }

        [Fact]
        public async Task ExpiredItemsCanNotBeChanged()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));
        }

        [Fact]
        public async Task CurrentlyPlayingItemsCannotBeChangedOrRemoved()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));

            await LoadGameplay(ContextUser);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));

            await FinishGameplay(ContextUser);
        }

        [Fact]
        public async Task OwnerChangesWhenItemChanges()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });
            Database.Setup(d => d.GetBeatmapAsync(4444)).ReturnsAsync(new database_beatmap { checksum = "4444" });

            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(USER_ID, room.Playlist[0].OwnerID);
            }

            await Hub.TransferHost(USER_ID_2);
            SetUserContext(ContextUser2);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 4444,
                BeatmapChecksum = "4444"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(USER_ID_2, room.Playlist[0].OwnerID);
            }
        }

        [Fact]
        public async Task PlayersCanNotReadyWithAllItemsExpired()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Assert.ThrowsAsync<InvalidStateException>(MarkCurrentUserReadyAndAvailable);
        }

        [Fact]
        public async Task PlayersUnReadiedWhenCurrentItemIsEdited()
        {
            Database.Setup(d => d.GetBeatmapAsync(3333)).ReturnsAsync(new database_beatmap { checksum = "3333" });

            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await MarkCurrentUserReadyAndAvailable();

            SetUserContext(ContextUser);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(MultiplayerUserState.Idle, room.Users[1].State);
            }
        }

        [Fact]
        public async Task SettingsChangedNotifiedBeforeItemIsRemoved()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1,
                BeatmapChecksum = "checksum",
            });

            int currentSequenceId = 0;
            int removedAtSequenceId = 0;
            int settingsChangedAtSequenceId = 0;

            Receiver.Setup(d => d.PlaylistItemRemoved(It.IsAny<long>()))
                    .Callback(() => removedAtSequenceId = ++currentSequenceId).CallBase();
            Receiver.Setup(d => d.SettingsChanged(It.IsAny<MultiplayerRoomSettings>()))
                    .Callback(() => settingsChangedAtSequenceId = ++currentSequenceId).CallBase();

            await Hub.RemovePlaylistItem(1);

            Assert.Equal(1, settingsChangedAtSequenceId);
            Assert.Equal(2, removedAtSequenceId);
        }
    }
}
