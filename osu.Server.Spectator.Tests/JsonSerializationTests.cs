// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using Newtonsoft.Json;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class JsonSerializationTests
    {
        private readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        };

        [Fact]
        public void TestMatchUserStateSerialization()
        {
            var state = new TeamVsMatchUserState
            {
                TeamID = 5,
            };

            var serialized = JsonConvert.SerializeObject(state, typeof(MatchRulesetUserState), settings);

            var deserializedState = JsonConvert.DeserializeObject(serialized, settings);
            var deserializedRoomState = deserializedState as TeamVsMatchUserState;

            Assert.NotNull(deserializedRoomState);

            Assert.Equal(state.TeamID, deserializedRoomState!.TeamID);
        }

        [Fact]
        public void TestMatchRoomStateSerialization()
        {
            var state = new TeamVsMatchRoomState
            {
                Teams =
                {
                    new MultiplayerTeam
                    {
                        ID = 1, Name = "test"
                    }
                }
            };
            var serialized = JsonConvert.SerializeObject(state, typeof(MatchRulesetRoomState), settings);

            var deserializedState = JsonConvert.DeserializeObject<MatchRulesetRoomState>(serialized, settings);
            var deserializedRoomState = deserializedState as TeamVsMatchRoomState;

            Assert.NotNull(deserializedRoomState);

            Assert.Equal(state.Teams.Count, deserializedRoomState!.Teams.Count);
            Assert.Equal(state.Teams.First().ID, deserializedRoomState.Teams.First().ID);
            Assert.Equal(state.Teams.First().Name, deserializedRoomState.Teams.First().Name);
        }

        [Fact]
        public void TestMultiplayerRoomSerialization()
        {
            MultiplayerRoom room = new MultiplayerRoom(1234)
            {
                Users =
                {
                    new MultiplayerRoomUser(888),
                }
            };

            var serialized = JsonConvert.SerializeObject(room, typeof(MatchRulesetRoomState), settings);

            var deserialisedRoom = JsonConvert.DeserializeObject<MultiplayerRoom>(serialized, settings);

            Assert.Equal(room.RoomID, deserialisedRoom!.RoomID);
            Assert.Equal(room.Users.Count, deserialisedRoom.Users.Count);
            Assert.Equal(room.Users.First().UserID, deserialisedRoom.Users.First().UserID);
        }
    }
}
