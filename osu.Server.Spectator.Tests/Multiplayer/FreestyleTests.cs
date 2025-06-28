// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Taiko.Mods;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class FreestyleTests : MultiplayerTest
    {
        #region AddItem

        /// <summary>
        /// Asserts that a freestyle playlist item can be added.
        /// </summary>
        [Fact]
        public async Task AddItem()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                RequiredMods = [new APIMod(new OsuModDoubleTime())],
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                AllowedMods = [new APIMod(new OsuModDoubleTime())],
            }));
        }

        #endregion

        #region SetUserStyle

        /// <summary>
        /// Asserts that the user can set their own style.
        /// </summary>
        [Fact]
        public async Task SetUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
            });

            // Set beatmap style.
            await Hub.ChangeUserStyle(12345, null);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(12345, room.Users.First().BeatmapId);
                Assert.Null(room.Users.First().RulesetId);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, null), Times.Once);
            }

            // Set ruleset style.
            await Hub.ChangeUserStyle(null, 3);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Equal(3, room.Users.First().RulesetId);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, 3), Times.Once);
            }

            // Set beatmap and ruleset style.
            await Hub.ChangeUserStyle(12345, 2);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(12345, room.Users.First().BeatmapId);
                Assert.Equal(2, room.Users.First().RulesetId);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, 12345, 2), Times.Once);
            }
        }

        /// <summary>
        /// Asserts that the user can not set a beatmap from another set.
        /// </summary>
        [Fact]
        public async Task SetUserStyle_InvalidBeatmapSetFails()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);

            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1 });
            Database.Setup(db => db.GetBeatmapsAsync(2)).ReturnsAsync(new[] { beatmap2 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
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
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync((database_beatmap?)null);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
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
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, playmode = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
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
            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                RequiredMods = [new APIMod(new OsuModDoubleTime())],
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                AllowedMods = [new APIMod(new OsuModHardRock())]
            }));
        }

        /// <summary>
        /// Asserts that user style is preserved when the host selects another beatmap from the same beatmap set.
        /// </summary>
        [Fact]
        public async Task EditItem_SameBeatmapSetPreservesUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };
            var beatmap3 = new database_beatmap { beatmap_id = 123456, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum3" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapAsync(123456)).ReturnsAsync(beatmap3);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2, beatmap3 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
            });

            // Set beatmap and ruleset style.
            await Hub.ChangeUserStyle(123456, 1);

            // Select another beatmap from the same set.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                Freestyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(123456, room.Users.First().BeatmapId);
                Assert.Equal(1, room.Users.First().RulesetId);
            }
        }

        /// <summary>
        /// Asserts that the user's ruleset style is preserved when a beatmap from a different set is selected and the ruleset remains valid.
        /// </summary>
        [Fact]
        public async Task EditItem_DifferentBeatmapSetPreservesRulesetStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 2, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };
            var beatmap3 = new database_beatmap { beatmap_id = 123456, beatmapset_id = 3, playmode = 3, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum3" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapAsync(123456)).ReturnsAsync(beatmap3);

            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1 });
            Database.Setup(db => db.GetBeatmapsAsync(2)).ReturnsAsync(new[] { beatmap2 });
            Database.Setup(db => db.GetBeatmapsAsync(3)).ReturnsAsync(new[] { beatmap3 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
            });

            // Set beatmap + ruleset style.
            await Hub.ChangeUserStyle(1234, 1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1234, room.Users.First().BeatmapId);
                Assert.Equal(1, room.Users.First().RulesetId);
            }

            // Select a beatmap from a different set that is still convertible.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                Freestyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Equal(1, room.Users.First().RulesetId);
            }

            // Set beatmap + ruleset style.
            await Hub.ChangeUserStyle(12345, 1);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(12345, room.Users.First().BeatmapId);
                Assert.Equal(1, room.Users.First().RulesetId);
            }

            // Select a beatmap from a different set that is inconvertible.
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum3",
                BeatmapID = 123456,
                RulesetID = 3,
                Freestyle = true
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Null(room.Users.First().RulesetId);
            }
        }

        /// <summary>
        /// Asserts that user styles are reset when freestyle is disabled.
        /// </summary>
        [Fact]
        public async Task EditItem_DisableFreestyleResetsUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
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

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Null(room.Users.First().RulesetId);
                Receiver.Verify(client => client.UserStyleChanged(USER_ID, null, null), Times.Once);
            }
        }

        #endregion

        #region CurrentItemChanged

        [Fact]
        public async Task CurrentItemChanged_SameBeatmapSetPreservesUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };
            var beatmap3 = new database_beatmap { beatmap_id = 123456, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum3" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapAsync(123456)).ReturnsAsync(beatmap3);

            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2, beatmap3 });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                Freestyle = true
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

                Assert.Equal(123456, room.Users.First().BeatmapId);
                Assert.Equal(1, room.Users.First().RulesetId);
            }
        }

        [Fact]
        public async Task CurrentItemChanged_DifferentBeatmapSetResetsUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 2, playmode = 3, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };
            var beatmap3 = new database_beatmap { beatmap_id = 123456, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum3" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapAsync(123456)).ReturnsAsync(beatmap3);

            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap3 });
            Database.Setup(db => db.GetBeatmapsAsync(2)).ReturnsAsync(new[] { beatmap2 });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapChecksum = "checksum2",
                BeatmapID = 12345,
                RulesetID = 3,
                Freestyle = true
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

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Null(room.Users.First().RulesetId);
            }
        }

        [Fact]
        public async Task CurrentItemChanged_FreestyleDisabledResetsUserStyle()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2 });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true
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

                Assert.Null(room.Users.First().BeatmapId);
                Assert.Null(room.Users.First().RulesetId);
            }
        }

        #endregion

        [Fact]
        public async Task UserModsAllowed()
        {
            var beatmap1 = new database_beatmap { beatmap_id = 1234, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" };
            var beatmap2 = new database_beatmap { beatmap_id = 12345, beatmapset_id = 1, approved = BeatmapOnlineStatus.Ranked, checksum = "checksum2" };

            Database.Setup(db => db.GetBeatmapAsync(1234)).ReturnsAsync(beatmap1);
            Database.Setup(db => db.GetBeatmapAsync(12345)).ReturnsAsync(beatmap2);
            Database.Setup(db => db.GetBeatmapsAsync(1)).ReturnsAsync(new[] { beatmap1, beatmap2 });

            await Hub.JoinRoom(ROOM_ID);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                BeatmapID = 1234,
                Freestyle = true,
                RequiredMods = [new APIMod(new OsuModHardRock())]
            });

            // Set user style + mods.
            await Hub.ChangeUserStyle(12345, 1);
            await Hub.ChangeUserMods(new[] { new APIMod(new TaikoModHidden()), new APIMod(new TaikoModConstantSpeed()) });
            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.Equal(["HD", "CS"], usage.Item!.Users.Single().Mods.Select(m => m.Acronym));

            // Try select invalid mod.
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[] { new APIMod(new TaikoModEasy()) }));
            using (var usage = await Hub.GetRoom(ROOM_ID))
                Assert.Equal(["HD", "CS"], usage.Item!.Users.Single().Mods.Select(m => m.Acronym));

            // Try change ruleset.
            Receiver.Invocations.Clear();
            await Hub.ChangeUserStyle(12345, 0);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                Assert.Equal(["HD"], usage.Item!.Users.Single().Mods.Select(m => m.Acronym));
                Receiver.Verify(u => u.UserModsChanged(USER_ID, It.IsAny<IEnumerable<APIMod>>()), Times.Once());
                Receiver.Verify(u => u.UserStyleChanged(USER_ID, 12345, 0), Times.Once);
            }
        }
    }
}
