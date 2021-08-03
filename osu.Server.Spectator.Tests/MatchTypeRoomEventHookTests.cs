// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    /// <summary>
    /// Tests covering propagation of events through <see cref="ServerMultiplayerRoom"/> to the <see cref="MultiplayerHub"/> via callbacks.
    /// </summary>
    public class MatchTypeRoomEventHookTests
    {
        [Fact]
        public void NewUserJoinedTriggersRulesetHook()
        {
            var room = new ServerMultiplayerRoom(1, new Mock<IMultiplayerServerMatchCallbacks>().Object);

            Mock<MatchTypeImplementation> typeImplementation = new Mock<MatchTypeImplementation>(room);
            room.MatchTypeImplementation = typeImplementation.Object;

            room.Users.Add(new MultiplayerRoomUser(1));

            typeImplementation.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public void UserLeavesTriggersRulesetHook()
        {
            var room = new ServerMultiplayerRoom(1, new Mock<IMultiplayerServerMatchCallbacks>().Object);
            var user = new MultiplayerRoomUser(1);

            room.Users.Add(user);

            Mock<MatchTypeImplementation> typeImplementation = new Mock<MatchTypeImplementation>(room);
            room.MatchTypeImplementation = typeImplementation.Object;

            room.Users.Remove(user);
            typeImplementation.Verify(m => m.HandleUserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public void TypeChangeTriggersInitialJoins()
        {
            var room = new ServerMultiplayerRoom(1, new Mock<IMultiplayerServerMatchCallbacks>().Object);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                room.Users.Add(new MultiplayerRoomUser(i));

            // change the match type
            Mock<MatchTypeImplementation> typeImplementation = new Mock<MatchTypeImplementation>(room);
            room.MatchTypeImplementation = typeImplementation.Object;

            // ensure the match type received hook events for all already joined users.
            typeImplementation.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Exactly(5));
        }
    }
}
