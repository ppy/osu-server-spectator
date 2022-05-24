// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class ModValidationTests : MultiplayerTest
    {
        [Fact]
        public async Task CanAddIncompatibleAllowedModsCombination()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                AllowedMods = new[]
                {
                    // setting an incompatible combination should be allowed.
                    // will be enforced at the point of a user choosing from the allowed mods.
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModApproachDifferent()),
                },
            });
        }

        [Fact]
        public async Task AddInvalidAllowedModsForRulesetThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                AllowedMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task AddInvalidRequiredModsCombinationThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                    new APIMod(new OsuModApproachDifferent()),
                },
            }));
        }

        [Fact]
        public async Task AddInvalidRequiredModsForRulesetThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task AddInvalidRequiredAllowedModsCombinationThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                },
                AllowedMods = new[]
                {
                    // allowed and required mods should always be cross-compatible.
                    new APIMod(new OsuModApproachDifferent()),
                },
            }));
        }

        [Fact(Skip = "needs dedupe check logic somewhere")]
        public async Task AddOverlappingRequiredAllowedModsFails()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RequiredMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                },
                AllowedMods = new[]
                {
                    // if a mod is in RequiredMods it shouldn't also be in AllowedMods.
                    new APIMod(new OsuModFlashlight()),
                },
            }));
        }

        [Fact]
        public async Task UserChangesMods()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            var setMods = new[] { new APIMod(new OsuModApproachDifferent()) };
            await Hub.ChangeUserMods(setMods);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(setMods, room.Users.First().Mods);
            }

            setMods = new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModFlashlight()) };
            await Hub.ChangeUserMods(setMods);

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(setMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsInvalidModCombinationThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModHidden()) }));

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(originalMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsDisallowedModsThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                RulesetID = 2,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new CatchModHidden()),
                },
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[]
            {
                new APIMod(new CatchModHidden()),
                // this should cause the complete setting change to fail, including the hidden mod application.
                new APIMod(new CatchModDaycore())
            }));

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Empty(room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task UserSelectsInvalidModsForRulesetThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                RulesetID = 2,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new CatchModHidden()),
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new CatchModHidden()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) }));

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(originalMods, room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task ChangingDisallowedModsRemovesUserMods()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent()),
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModHardRock())
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModFlashlight()), new APIMod(new OsuModApproachDifferent()) });
            await assertUserMods(USER_ID, "FL", "AD");

            SetUserContext(ContextUser2);
            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModHardRock()), new APIMod(new OsuModApproachDifferent()) });
            await assertUserMods(USER_ID_2, "HR", "AD");

            SetUserContext(ContextUser);
            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModHardRock())
                },
            });

            await assertUserMods(USER_ID, "FL");
            await assertUserMods(USER_ID_2, "HR");

            async Task assertUserMods(int userId, params string[] modAcronyms)
            {
                Receiver.Verify(c =>
                    c.UserModsChanged(userId, It.Is<IEnumerable<APIMod>>(mods =>
                        mods.Select(m => m.Acronym).SequenceEqual(modAcronyms))), Times.Once);

                using (var usage = await Hub.GetRoom(ROOM_ID))
                {
                    var room = usage.Item;
                    Debug.Assert(room != null);

                    var userMods = room.Users.Single(u => u.UserID == userId).Mods;
                    Assert.Equal(modAcronyms, userMods.Select(m => m.Acronym));
                }
            }
        }

        [Fact]
        public async Task ChangingRulesetRemovesInvalidUserMods()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.NotEmpty(room.Users.First().Mods);
            }

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                RulesetID = 2,
                BeatmapChecksum = "checksum",
            });

            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Empty(room.Users.First().Mods);
            }
        }

        [Fact]
        public async Task AddUserUnplayableModThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[] { new APIMod(new OsuModAutoplay()) },
            }));

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                AllowedMods = new[] { new APIMod(new OsuModAutoplay()) },
            }));
        }

        [Fact]
        public async Task AddMultiplayerUnplayableModThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[] { new APIMod(new ModAdaptiveSpeed()) },
            }));

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                AllowedMods = new[] { new APIMod(new ModAdaptiveSpeed()) },
            }));
        }

        [Fact]
        public async Task AddMultiplayerInvalidFreeModThrowsOnAllowedModsOnly()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                RequiredMods = new[] { new APIMod(new OsuModDoubleTime()) },
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.EditPlaylistItem(new MultiplayerPlaylistItem
            {
                ID = 1,
                BeatmapChecksum = "checksum",
                RulesetID = 0,
                AllowedMods = new[] { new APIMod(new OsuModDoubleTime()) },
            }));
        }
    }
}
