// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class JsonSerializationTests
    {
        private readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new SignalRDerivedTypeWorkaroundJsonConverter(),
            },
        };

        [Fact]
        public void TestMatchUserStateSerialization()
        {
            var state = new TeamVersusUserState
            {
                TeamID = 5,
            };

            var serialized = JsonConvert.SerializeObject(state, settings);

            var deserializedState = JsonConvert.DeserializeObject<MatchUserState>(serialized, settings);
            var deserializedRoomState = deserializedState as TeamVersusUserState;

            Assert.NotNull(deserializedRoomState);

            Assert.Equal(state.TeamID, deserializedRoomState.AsNonNull().TeamID);
        }

        [Fact]
        public void TestMatchRoomStateSerialization()
        {
            var state = new TeamVersusRoomState
            {
                Teams =
                {
                    new MultiplayerTeam
                    {
                        ID = 1, Name = "test"
                    }
                }
            };
            var serialized = JsonConvert.SerializeObject(state, settings);

            var deserializedState = JsonConvert.DeserializeObject<MatchRoomState>(serialized, settings).AsNonNull();

            var teamVersusRoomState = (TeamVersusRoomState)deserializedState;

            Assert.Equal(state.Teams.Count, teamVersusRoomState.Teams.Count);
            Assert.Equal(state.Teams.First().ID, teamVersusRoomState.Teams.First().ID);
            Assert.Equal(state.Teams.First().Name, teamVersusRoomState.Teams.First().Name);
        }

        [Fact]
        public void TestMultiplayerRoomSerialization()
        {
            MultiplayerRoom room = new MultiplayerRoom(1234)
            {
                MatchState = new TeamVersusRoomState(),
                Users =
                {
                    new MultiplayerRoomUser(888),
                }
            };

            var serialized = JsonConvert.SerializeObject(room, settings);

            var deserialisedRoom = JsonConvert.DeserializeObject<MultiplayerRoom>(serialized, settings).AsNonNull();

            Assert.Equal(room.RoomID, deserialisedRoom.RoomID);
            Assert.Equal(room.Users.Count, deserialisedRoom.Users.Count);
            Assert.Equal(room.Users.First().UserID, deserialisedRoom.Users.First().UserID);
            Assert.Equal(typeof(TeamVersusRoomState), deserialisedRoom.MatchState?.GetType());
        }
    }
}
