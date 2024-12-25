// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Beatmaps;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class UserStyleTests : MultiplayerTest
    {
        [Fact]
        public async Task AddItem()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });
        }

        [Fact]
        public async Task EditItem()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });
        }

        [Fact]
        public async Task SetStyle()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(12345, null);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 12345);
                Assert.Equal(room.Users.First().RulesetId, null);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, null), Times.Once);
            }

            await Hub.ChangeUserStyle(1234, 3);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 1234);
                Assert.Equal(room.Users.First().RulesetId, 3);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 1234, 3), Times.Once);
            }
        }

        [Fact]
        public async Task CanNotAddItem_DifferentBeatmapIdAndSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 2
            }));
        }

        [Fact]
        public async Task CanNotEditItem_DifferentBeatmapIdAndSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 2
            }));
        }

        [Fact]
        public async Task CanNotSetStyle_FreeStyleNotAllowed()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(null, 1));
        }

        [Fact]
        public async Task CanNotSetStyle_DifferentBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, null));
            Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task CanNotSetStyle_UnknownBeatmap()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync((database_beatmap?)null);

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, null));
            Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task CanNotSetStyle_InvalidRulesetId()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, playmode = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1,
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(null, -1));
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(null, 4));
            await Hub.ChangeUserStyle(null, 0);
            await Hub.ChangeUserStyle(null, 1);
            await Hub.ChangeUserStyle(null, 2);
            await Hub.ChangeUserStyle(null, 3);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 0), Times.Once);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 1), Times.Once);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 2), Times.Once);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 3), Times.Once);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 0));
            await Hub.ChangeUserStyle(12345, 1);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 2));
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 3));
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 0), Times.Never);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 1), Times.Once);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 2), Times.Never);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 3), Times.Never);
        }

        [Fact]
        public async Task StyleNotReset_OnSameBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(null, 1);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                BeatmapSetID = 1
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, 1);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, null), Times.Never);
            }
        }

        [Fact]
        public async Task StyleReset_OnNullBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(null, 1);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, null);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, null), Times.Once);
            }
        }

        [Fact]
        public async Task RulesetStyleNotReset_OnDifferentBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(null, 1);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                BeatmapSetID = 2
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }
        }

        [Fact]
        public async Task RulesetStyleReset_OnInvalidRuleset()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, playmode = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(null, 2);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                BeatmapSetID = 1,
                RulesetID = 1
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, null);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, null), Times.Once);
            }
        }

        [Fact]
        public async Task StyleNotReset_AfterGameplay_WithSameBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(123456))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(123456, 1);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 123456);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }
        }

        [Fact]
        public async Task BeatmapStyleReset_AfterGameplay_WithDifferentBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(123456))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                BeatmapSetID = 2
            });

            await Hub.ChangeUserStyle(123456, 1);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }
        }

        [Fact]
        public async Task RulesetStyleReset_AfterGameplay_WithInvalidRuleset()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, playmode = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                RulesetID = 1,
                BeatmapSetID = 1
            });

            await Hub.ChangeUserStyle(null, 2);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, null);
            }
        }

        [Fact]
        public async Task StyleReset_AfterGameplay_WithNullBeatmapSet()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                BeatmapSetID = 1
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234
            });

            await Hub.ChangeUserStyle(12345, 1);

            await MarkCurrentUserReadyAndAvailable();
            await Hub.StartMatch();
            await LoadAndFinishGameplay(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, null);
            }
        }
    }
}
