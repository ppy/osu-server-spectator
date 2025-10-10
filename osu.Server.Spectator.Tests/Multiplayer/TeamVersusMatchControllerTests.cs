// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Standard;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TeamVersusMatchControllerTests : MultiplayerTest
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task UserRequestsValidTeamChange(int team)
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = await ServerMultiplayerRoom.InitialiseAsync(ROOM_ID, hub.Object, DatabaseFactory.Object);

            var teamVersus = new TeamVersusMatchController(room, hub.Object, DatabaseFactory.Object);

            // change the match type
            await room.ChangeMatchType(teamVersus);

            var user = new MultiplayerRoomUser(1);

            await room.AddUser(user);
            hub.Verify(h => h.NotifyMatchUserStateChanged(room, user), Times.Once());

            await teamVersus.HandleUserRequest(user, new ChangeTeamRequest { TeamID = team });

            checkUserOnTeam(user, team);
            hub.Verify(h => h.NotifyMatchUserStateChanged(room, user), Times.Exactly(2));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task UserRequestsInvalidTeamChange(int team)
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = await ServerMultiplayerRoom.InitialiseAsync(ROOM_ID, hub.Object, DatabaseFactory.Object);

            var teamVersus = new TeamVersusMatchController(room, hub.Object, DatabaseFactory.Object);

            // change the match type
            await room.ChangeMatchType(teamVersus);

            var user = new MultiplayerRoomUser(1);

            await room.AddUser(user);
            // called once on the initial user join operation (to inform other clients in the room).
            hub.Verify(h => h.NotifyMatchUserStateChanged(room, user), Times.Once());

            var previousTeam = ((TeamVersusUserState)user.MatchState!).TeamID;

            await Assert.ThrowsAsync<InvalidStateException>(() => teamVersus.HandleUserRequest(user, new ChangeTeamRequest { TeamID = team }));

            checkUserOnTeam(user, previousTeam);
            // was not called a second time from the invalid change.
            hub.Verify(h => h.NotifyMatchUserStateChanged(room, user), Times.Once());
        }

        [Fact]
        public async Task NewUsersAssignedToTeamWithFewerUsers()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = await ServerMultiplayerRoom.InitialiseAsync(ROOM_ID, hub.Object, DatabaseFactory.Object);

            // change the match type
            await room.ChangeMatchType(MatchType.TeamVersus);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                await room.AddUser(new MultiplayerRoomUser(i));

            // change all users to team 0
            for (int i = 0; i < 5; i++)
                await room.Controller.HandleUserRequest(room.Users[i], new ChangeTeamRequest { TeamID = 0 });

            Assert.All(room.Users, u => checkUserOnTeam(u, 0));

            for (int i = 5; i < 10; i++)
            {
                var newUser = new MultiplayerRoomUser(i);

                await room.AddUser(newUser);

                // all new users should be joined to team 1 to balance the user counts.
                checkUserOnTeam(newUser, 1);
            }
        }

        [Fact]
        public async Task InitialUsersAssignedToTeamsEqually()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = await ServerMultiplayerRoom.InitialiseAsync(ROOM_ID, hub.Object, DatabaseFactory.Object);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                await room.AddUser(new MultiplayerRoomUser(i));

            // change the match type
            await room.ChangeMatchType(MatchType.TeamVersus);

            checkUserOnTeam(room.Users[0], 0);
            checkUserOnTeam(room.Users[1], 1);
            checkUserOnTeam(room.Users[2], 0);
            checkUserOnTeam(room.Users[3], 1);
            checkUserOnTeam(room.Users[4], 0);
        }

        [Fact]
        public async Task StateMaintainedBetweenRulesetSwitch()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = await ServerMultiplayerRoom.InitialiseAsync(ROOM_ID, hub.Object, DatabaseFactory.Object);

            await room.ChangeMatchType(MatchType.TeamVersus);

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                await room.AddUser(new MultiplayerRoomUser(i));

            checkUserOnTeam(room.Users[0], 0);
            checkUserOnTeam(room.Users[1], 1);
            checkUserOnTeam(room.Users[2], 0);
            checkUserOnTeam(room.Users[3], 1);
            checkUserOnTeam(room.Users[4], 0);

            // change the match type
            await room.ChangeMatchType(MatchType.HeadToHead);
            await room.ChangeMatchType(MatchType.TeamVersus);

            checkUserOnTeam(room.Users[0], 0);
            checkUserOnTeam(room.Users[1], 1);
            checkUserOnTeam(room.Users[2], 0);
            checkUserOnTeam(room.Users[3], 1);
            checkUserOnTeam(room.Users[4], 0);
        }

        private void checkUserOnTeam(MultiplayerRoomUser u, int team) =>
            Assert.Equal(team, (u.MatchState as TeamVersusUserState)?.TeamID);
    }
}
