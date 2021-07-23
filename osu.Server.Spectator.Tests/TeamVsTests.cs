// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchRulesets.TeamVs;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class TeamVsTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void UserRequestsValidTeamChange(int team)
        {
            var hubCallbacks = new Mock<IMultiplayerServerMatchRulesetCallbacks>();
            var room = new ServerMultiplayerRoom(1, hubCallbacks.Object);
            var teamVs = new TeamVsRuleset(room);

            // change the match ruleset
            room.MatchRuleset = teamVs;

            var user = new MultiplayerRoomUser(1);

            room.Users.Add(user);

            teamVs.HandleUserRequest(user, new ChangeTeamRequest { TeamID = team });

            checkUserOnTeam(user, team);
            hubCallbacks.Verify(h => h.UpdateMatchRulesetUserState(room, user), Times.Once());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(3)]
        public void UserRequestsInvalidTeamChange(int team)
        {
            var hubCallbacks = new Mock<IMultiplayerServerMatchRulesetCallbacks>();
            var room = new ServerMultiplayerRoom(1, hubCallbacks.Object);
            var teamVs = new TeamVsRuleset(room);

            // change the match ruleset
            room.MatchRuleset = teamVs;

            var user = new MultiplayerRoomUser(1);

            room.Users.Add(user);

            var previousTeam = ((TeamVsMatchUserState)user.MatchRulesetState!).TeamID;

            Assert.Throws<InvalidStateException>(() => teamVs.HandleUserRequest(user, new ChangeTeamRequest { TeamID = team }));

            checkUserOnTeam(user, previousTeam);
            hubCallbacks.Verify(h => h.UpdateMatchRulesetUserState(room, user), Times.Never());
        }

        [Fact]
        public void NewUsersAssignedToTeamWithFewerUsers()
        {
            var room = new ServerMultiplayerRoom(1, new Mock<IMultiplayerServerMatchRulesetCallbacks>().Object);
            var teamVs = new TeamVsRuleset(room);

            // change the match ruleset
            room.MatchRuleset = teamVs;

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                room.Users.Add(new MultiplayerRoomUser(i));

            // change all users to team 0
            for (int i = 0; i < 5; i++)
                teamVs.HandleUserRequest(room.Users[i], new ChangeTeamRequest { TeamID = 0 });

            Assert.All(room.Users, u => checkUserOnTeam(u, 0));

            for (int i = 5; i < 10; i++)
            {
                var newUser = new MultiplayerRoomUser(i);

                room.Users.Add(newUser);

                // all new users should be joined to team 1 to balance the user counts.
                checkUserOnTeam(newUser, 1);
            }
        }

        [Fact]
        public void InitialUsersAssignedToTeamsEqually()
        {
            var room = new ServerMultiplayerRoom(1, new Mock<IMultiplayerServerMatchRulesetCallbacks>().Object);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                room.Users.Add(new MultiplayerRoomUser(i));

            // change the match ruleset
            room.MatchRuleset = new TeamVsRuleset(room);

            checkUserOnTeam(room.Users[0], 0);
            checkUserOnTeam(room.Users[1], 1);
            checkUserOnTeam(room.Users[2], 0);
            checkUserOnTeam(room.Users[3], 1);
            checkUserOnTeam(room.Users[4], 0);
        }

        private void checkUserOnTeam(MultiplayerRoomUser u, int team) =>
            Assert.Equal(team, (u.MatchRulesetState as TeamVsMatchUserState)?.TeamID);
    }
}
