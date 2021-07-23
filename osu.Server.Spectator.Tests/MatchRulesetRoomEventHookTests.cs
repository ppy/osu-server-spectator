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
    public class MatchRulesetRoomEventHookTests
    {
        [Fact]
        public void NewUserJoinedTriggersRulesetHook()
        {
            var room = new ServerMultiplayerRoom(1);

            Mock<MatchRuleset> matchRuleset = new Mock<MatchRuleset>(room);
            room.MatchRuleset = matchRuleset.Object;

            room.Users.Add(new MultiplayerRoomUser(1));

            matchRuleset.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public void UserLeavesTriggersRulesetHook()
        {
            var room = new ServerMultiplayerRoom(1);
            var user = new MultiplayerRoomUser(1);

            room.Users.Add(user);

            Mock<MatchRuleset> matchRuleset = new Mock<MatchRuleset>(room);
            room.MatchRuleset = matchRuleset.Object;

            room.Users.Remove(user);
            matchRuleset.Verify(m => m.HandleUserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public void MatchRulesetChangeTriggersInitialJoins()
        {
            var room = new ServerMultiplayerRoom(1);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                room.Users.Add(new MultiplayerRoomUser(i));

            // change the match ruleset
            Mock<MatchRuleset> matchRuleset = new Mock<MatchRuleset>(room);
            room.MatchRuleset = matchRuleset.Object;

            // ensure the match ruleset received hook events for all already joined users.
            matchRuleset.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Exactly(5));
        }
    }
}
