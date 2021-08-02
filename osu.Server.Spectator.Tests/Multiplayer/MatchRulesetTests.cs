// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MatchRulesetTests : MultiplayerTest
    {
        [Fact]
        public async Task MatchRulesetRoomStateUpdatePropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var mockRoomState = new Mock<MatchRulesetRoomState>();

                room.MatchRulesetState = mockRoomState.Object;

                await room.UpdateMatchRulesetRoomState(room);

                Receiver.Verify(c => c.MatchRulesetRoomStateChanged(mockRoomState.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchRulesetEventPropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var mockEvent = new Mock<MatchRulesetServerEvent>();

                await room.SendMatchRulesetEvent(room, mockEvent.Object);

                Receiver.Verify(c => c.MatchRulesetEvent(mockEvent.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchRulesetUserStateUpdatePropagatesToUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var roomUsage = Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);

                var mockRoomState = new Mock<MatchRulesetUserState>();

                var user = room.Users.First();

                user.MatchRulesetState = mockRoomState.Object;

                await room.UpdateMatchRulesetUserState(room, user);

                Receiver.Verify(c => c.MatchRulesetUserStateChanged(user.UserID, mockRoomState.Object), Times.Once);
            }
        }

        [Fact]
        public async Task MatchRulesetUserRequestForwardsToMatchRuleset()
        {
            Mock<MatchRuleset> matchRuleset;

            await Hub.JoinRoom(ROOM_ID);

            using (var roomUsage = Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);

                matchRuleset = new Mock<MatchRuleset>(room);
                room.MatchRuleset = matchRuleset.Object;
            }

            var mockRequest = new Mock<MatchRulesetUserRequest>();

            await Hub.SendMatchRulesetRequest(mockRequest.Object);

            using (var roomUsage = Hub.GetRoom(ROOM_ID))
            {
                var room = roomUsage.Item;
                Debug.Assert(room != null);
                matchRuleset.Verify(r => r.HandleUserRequest(room.Users.First(), mockRequest.Object), Times.Once());
            }
        }

        [Fact]
        public async Task ChangeMatchRulesetType()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                // initially default
                Assert.Equal(MatchRulesetType.HeadToHead, room.Settings.MatchRulesetType);
            }

            MultiplayerRoomSettings testSettings = new MultiplayerRoomSettings
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                MatchRulesetType = MatchRulesetType.TeamVs,
            };

            await Hub.ChangeSettings(testSettings);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.True(room.MatchRuleset is TeamVsRuleset);
                Assert.Equal(MatchRulesetType.TeamVs, room.Settings.MatchRulesetType);

                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }

            Receiver.Verify(r => r.MatchRulesetUserStateChanged(USER_ID, It.IsAny<TeamVsMatchUserState>()), Times.Once);
        }
    }
}
