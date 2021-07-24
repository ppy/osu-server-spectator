// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Osu.Mods;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class ModValidationTest : MultiplayerTest
    {
        [Fact]
        public async Task HostCanSetIncompatibleAllowedModsCombination()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
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
        public async Task HostSetsInvalidAllowedModsForRulesetThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                AllowedMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task HostSetsInvalidRequiredModsCombinationThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
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
        public async Task HostSetsInvalidRequiredModsForRulesetThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                RulesetID = 3,
                RequiredMods = new[]
                {
                    new APIMod(new OsuModBlinds()),
                },
            }));
        }

        [Fact]
        public async Task HostSetsInvalidRequiredAllowedModsCombinationThrows()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
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
        public async Task HostSetsOverlappingRequiredAllowedMods()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeSettings(new MultiplayerRoomSettings
            {
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

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            var setMods = new[] { new APIMod(new OsuModApproachDifferent()) };
            await Hub.ChangeUserMods(setMods);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Equal(setMods, room.Users.First().Mods);
            }

            setMods = new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModFlashlight()) };
            await Hub.ChangeUserMods(setMods);

            using (var usage = Hub.GetRoom(ROOM_ID))
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

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModHidden()),
                    new APIMod(new OsuModApproachDifferent())
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()), new APIMod(new OsuModHidden()) }));

            using (var usage = Hub.GetRoom(ROOM_ID))
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

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
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

            using (var usage = Hub.GetRoom(ROOM_ID))
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

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                RulesetID = 2,
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new CatchModHidden()),
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new CatchModHidden()) });

            IEnumerable<APIMod> originalMods;

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                originalMods = room.Users.First().Mods;
                Assert.NotEmpty(originalMods);
            }

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) }));

            using (var usage = Hub.GetRoom(ROOM_ID))
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
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent()),
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModHardRock())
                },
            });

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModFlashlight()), new APIMod(new OsuModApproachDifferent()) });
            assertUserMods(USER_ID, "FL", "AD");

            SetUserContext(ContextUser2);
            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModHardRock()), new APIMod(new OsuModApproachDifferent()) });
            assertUserMods(USER_ID_2, "HR", "AD");

            SetUserContext(ContextUser);
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModFlashlight()),
                    new APIMod(new OsuModHardRock())
                },
            });

            assertUserMods(USER_ID, "FL");
            assertUserMods(USER_ID_2, "HR");

            void assertUserMods(int userId, params string[] modAcronyms)
            {
                Receiver.Verify(c =>
                    c.UserModsChanged(userId, It.Is<IEnumerable<APIMod>>(mods =>
                        mods.Select(m => m.Acronym).SequenceEqual(modAcronyms))), Times.Once);

                using (var usage = Hub.GetRoom(ROOM_ID))
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

            var roomSettings = new MultiplayerRoomSettings
            {
                BeatmapChecksum = "checksum",
                AllowedMods = new[]
                {
                    new APIMod(new OsuModApproachDifferent())
                },
            };

            await Hub.ChangeSettings(roomSettings);

            await Hub.ChangeUserMods(new[] { new APIMod(new OsuModApproachDifferent()) });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.NotEmpty(room.Users.First().Mods);
            }

            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                RulesetID = 2,
                BeatmapChecksum = "checksum",
            });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);
                Assert.Empty(room.Users.First().Mods);
            }
        }
    }
}
