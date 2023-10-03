// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer;

public class MultiplayerInviteTest : MultiplayerTest
{
    [Fact]
    public async Task UserCanInviteFriends()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserRelation(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new phpbb_zebra { friend = true });

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(USER_ID_2);

        invitedUserReceiver.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            ""
        ), Times.Once);
    }

    [Fact]
    public async Task UserCantInviteUserTheyBlocked()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserRelation(USER_ID, USER_ID_2)).ReturnsAsync(new phpbb_zebra { foe = true });
        Database.Setup(d => d.GetUserRelation(USER_ID_2, USER_ID)).ReturnsAsync(new phpbb_zebra { friend = true });

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlockedException>(() => Hub.InvitePlayer(USER_ID_2));

        invitedUserReceiver.Verify(r => r.Invited(
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<string>()
        ), Times.Never);
    }

    [Fact]
    public async Task UserCantInviteUserTheyAreBlockedBy()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserRelation(USER_ID, USER_ID_2)).ReturnsAsync(new phpbb_zebra { friend = true });
        Database.Setup(d => d.GetUserRelation(USER_ID_2, USER_ID)).ReturnsAsync(new phpbb_zebra { foe = true });

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlockedException>(() => Hub.InvitePlayer(USER_ID_2));

        invitedUserReceiver.Verify(r => r.Invited(
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<string>()
        ), Times.Never);
    }

    [Fact]
    public async Task UserCantInviteUserWithDisabledPMs()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserAllowsPMs(USER_ID_2)).ReturnsAsync(false);

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlocksPMsException>(() => Hub.InvitePlayer(USER_ID_2));

        invitedUserReceiver.Verify(r => r.Invited(
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<string>()
        ), Times.Never);
    }

    [Fact]
    public async Task UserCantInviteRestrictedUser()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserRelation(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new phpbb_zebra { friend = true });
        Database.Setup(d => d.IsUserRestrictedAsync(It.IsAny<int>())).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<InvalidStateException>(() => Hub.InvitePlayer(USER_ID_2));

        invitedUserReceiver.Verify(r => r.Invited(
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<string>()
        ), Times.Never);
    }

    [Fact]
    public async Task UserCanInviteUserWithEnabledPMs()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserAllowsPMs(USER_ID_2)).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(USER_ID_2);

        invitedUserReceiver.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            ""
        ), Times.Once);
    }

    [Fact]
    public async Task UserCanInviteIntoRoomWithPassword()
    {
        Database.Setup(db => db.GetRoomAsync(It.IsAny<long>()))
                .Callback<long>(InitialiseRoom)
                .ReturnsAsync(new multiplayer_room
                {
                    password = "password",
                    user_id = USER_ID
                });

        SetUserContext(ContextUser);
        await Hub.JoinRoomWithPassword(ROOM_ID, "password");

        var invitedUserReceiver = new Mock<IMultiplayerClient>();
        Clients.Setup(clients => clients.User(USER_ID_2.ToString())).Returns(invitedUserReceiver.Object);

        Database.Setup(d => d.GetUserAllowsPMs(USER_ID_2)).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(USER_ID_2);

        invitedUserReceiver.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            "password"
        ), Times.Once);
    }
}
