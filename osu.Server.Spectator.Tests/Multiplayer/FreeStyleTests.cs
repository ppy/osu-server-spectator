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
    public class FreeStyleTests : MultiplayerTest
    {
        #region AddItem

        /// <summary>
        /// Asserts that a freestyle playlist item can be added.
        /// </summary>
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
                FreeStyle = true
            });
        }

        #endregion

        #region SetUserStyle

        /// <summary>
        /// Asserts that the user can set their own style.
        /// </summary>
        [Fact]
        public async Task SetUserStyle()
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
                FreeStyle = true
            });

            // Set beatmap style.
            await Hub.ChangeUserStyle(12345, null);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 12345);
                Assert.Equal(room.Users.First().RulesetId, null);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, null), Times.Once);
            }

            // Set ruleset style.
            await Hub.ChangeUserStyle(null, 3);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, 3);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 3), Times.Once);
            }

            // Set beatmap and ruleset style.
            await Hub.ChangeUserStyle(12345, 2);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 12345);
                Assert.Equal(room.Users.First().RulesetId, 2);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 2), Times.Once);
            }
        }

        /// <summary>
        /// Asserts that the user can not set a beatmap from another set.
        /// </summary>
        [Fact]
        public async Task SetUserStyle_InvalidBeatmapSetFails()
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
                FreeStyle = true
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, null));
            Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
        }

        /// <summary>
        /// Asserts that the user can not set a beatmap that doesn't exist.
        /// </summary>
        [Fact]
        public async Task SetUserStyle_UnknownBeatmapFails()
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
                FreeStyle = true
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, null));
            Receiver.Verify(client => client.UserStyleChanged(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
        }

        /// <summary>
        /// Asserts that the user can only set a ruleset allowed by the beatmap, given by whether the beatmap can be converted or not.
        /// </summary>
        [Fact]
        public async Task SetUserStyle_InvalidRulesetIdFails()
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
                FreeStyle = true,
            });

            // Out of range
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(null, -1));
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(null, 4));

            // Convertible
            await Hub.ChangeUserStyle(null, 0);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 0), Times.Once);
            await Hub.ChangeUserStyle(null, 1);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 1), Times.Once);
            await Hub.ChangeUserStyle(null, 2);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 2), Times.Once);
            await Hub.ChangeUserStyle(null, 3);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 3), Times.Once);

            // Inconvertible
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 0));
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 0), Times.Never);
            await Hub.ChangeUserStyle(12345, 1);
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 1), Times.Once);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 2));
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 2), Times.Never);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserStyle(12345, 3));
            Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 3), Times.Never);
        }

        #endregion

        #region EditItem

        /// <summary>
        /// Asserts that a playlist item can be edited to become freestyle.
        /// </summary>
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
                FreeStyle = true
            });
        }

        /// <summary>
        /// Asserts that user style is preserved when the host selects another beatmap from the same beatmap set.
        /// </summary>
        [Fact]
        public async Task EditItem_SameBeatmapSetPreservesUserStyle()
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
                FreeStyle = true
            });

            // Set beatmap and ruleset style.
            await Hub.ChangeUserStyle(123456, 1);

            // Select another beatmap from the same set.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                FreeStyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 123456);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }
        }

        /// <summary>
        /// Asserts that the user's ruleset style is preserved when a beatmap from a different set is selected and the ruleset remains valid.
        /// </summary>
        [Fact]
        public async Task EditItem_DifferentBeatmapSetPreservesRulesetStyle()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" });

            Database.Setup(db => db.GetBeatmapAsync(123456))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 3, playmode = 3, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum3" });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                FreeStyle = true
            });

            // Set beatmap + ruleset style.
            await Hub.ChangeUserStyle(1234, 1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 1234);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }

            // Select a beatmap from a different set that is still convertible.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                FreeStyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }

            // Set beatmap + ruleset style.
            await Hub.ChangeUserStyle(12345, 1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, 12345);
                Assert.Equal(room.Users.First().RulesetId, 1);
            }

            // Select a beatmap from a different set that is inconvertible.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum3",
                BeatmapID = 123456,
                RulesetID = 3,
                FreeStyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(room.Users.First().BeatmapId, null);
                Assert.Equal(room.Users.First().RulesetId, null);
            }
        }

        /// <summary>
        /// Asserts that user styles are reset when freestyle is disabled.
        /// </summary>
        [Fact]
        public async Task EditItem_DisableFreeStyleResetsUserStyle()
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
                FreeStyle = true
            });

            // Set beatmap and ruleset style.
            await Hub.ChangeUserStyle(12345, 1);

            // Disable freestyle.
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

        #endregion

        #region CurrentItemChanged

        [Fact]
        public async Task CurrentItemChanged_SameBeatmapSetPreservesUserStyle()
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
                FreeStyle = true
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                FreeStyle = true
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
        public async Task CurrentItemChanged_DifferentBeatmapSetResetsUserStyle()
        {
            Database.Setup(db => db.GetBeatmapAsync(1234))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(12345))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 2, playmode = 3, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            Database.Setup(db => db.GetBeatmapAsync(123456))
                    .ReturnsAsync(new database_beatmap { beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                FreeStyle = true
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 12345,
                RulesetID = 3,
                FreeStyle = true
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
                Assert.Equal(room.Users.First().RulesetId, null);
            }
        }

        [Fact]
        public async Task CurrentItemChanged_FreeStyleDisabledResetsUserStyle()
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
                FreeStyle = true
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

        #endregion
    }
}
