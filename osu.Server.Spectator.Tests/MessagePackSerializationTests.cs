// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using MessagePack;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class MessagePackSerializationTests
    {
        [Fact(Skip = "Won't work without abstract class definitions (temporarily removed).")]
        public void TestMatchUserStateSerialization()
        {
            var state = new TeamVersusUserState
            {
                TeamID = 5,
            };

            var serialized = MessagePackSerializer.Serialize((MatchUserState)state);

            var deserializedState = MessagePackSerializer.Deserialize<MatchUserState>(serialized);
            var deserializedRoomState = deserializedState as TeamVersusUserState;

            Assert.NotNull(deserializedRoomState);

            Assert.Equal(state.TeamID, deserializedRoomState.TeamID);
        }

        [Fact(Skip = "Won't work without abstract class definitions (temporarily removed).")]
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
            var serialized = MessagePackSerializer.Serialize((MatchRoomState)state);

            var deserializedState = MessagePackSerializer.Deserialize<MatchRoomState>(serialized);
            var deserializedRoomState = deserializedState as TeamVersusRoomState;

            Assert.NotNull(deserializedRoomState);

            Assert.Equal(state.Teams.Count, deserializedRoomState.Teams.Count);
            Assert.Equal(state.Teams.First().ID, deserializedRoomState.Teams.First().ID);
            Assert.Equal(state.Teams.First().Name, deserializedRoomState.Teams.First().Name);
        }

        [Fact(Skip = "Won't work without abstract class definitions (temporarily removed).")]
        public void TestMultiplayerRoomSerialization()
        {
            MultiplayerRoom room = new MultiplayerRoom(1234)
            {
                Users =
                {
                    new MultiplayerRoomUser(888),
                }
            };

            var serialized = MessagePackSerializer.Serialize(room);

            var deserialisedRoom = MessagePackSerializer.Deserialize<MultiplayerRoom>(serialized);

            Assert.Equal(room.RoomID, deserialisedRoom.RoomID);
            Assert.Equal(room.Users.Count, deserialisedRoom.Users.Count);
            Assert.Equal(room.Users.First().UserID, deserialisedRoom.Users.First().UserID);
        }
    }
}
