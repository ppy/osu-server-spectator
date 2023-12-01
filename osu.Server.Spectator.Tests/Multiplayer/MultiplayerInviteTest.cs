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
    private const int invited_user_id = 3;
    private readonly Mock<DelegatingMultiplayerClient> invitedUser;

    public MultiplayerInviteTest()
    {
        CreateUser(invited_user_id, out _, out invitedUser);
    }

    [Fact]
    public async Task UserCanInviteFriends()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        Database.Setup(d => d.GetUserRelation(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new phpbb_zebra { friend = true });

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(invited_user_id);

        invitedUser.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            string.Empty
        ), Times.Once);
    }

    [Fact]
    public async Task UserCantInviteUserTheyBlocked()
    {
        SetUserContext(ContextUser);
        await Hub.JoinRoom(ROOM_ID);

        Database.Setup(d => d.GetUserRelation(USER_ID, invited_user_id)).ReturnsAsync(new phpbb_zebra { foe = true });
        Database.Setup(d => d.GetUserRelation(invited_user_id, USER_ID)).ReturnsAsync(new phpbb_zebra { friend = true });

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlockedException>(() => Hub.InvitePlayer(invited_user_id));

        invitedUser.Verify(r => r.Invited(
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

        Database.Setup(d => d.GetUserRelation(USER_ID, invited_user_id)).ReturnsAsync(new phpbb_zebra { friend = true });
        Database.Setup(d => d.GetUserRelation(invited_user_id, USER_ID)).ReturnsAsync(new phpbb_zebra { foe = true });

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlockedException>(() => Hub.InvitePlayer(invited_user_id));

        invitedUser.Verify(r => r.Invited(
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

        Database.Setup(d => d.GetUserAllowsPMs(invited_user_id)).ReturnsAsync(false);

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<UserBlocksPMsException>(() => Hub.InvitePlayer(invited_user_id));

        invitedUser.Verify(r => r.Invited(
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

        Database.Setup(d => d.GetUserRelation(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new phpbb_zebra { friend = true });
        Database.Setup(d => d.IsUserRestrictedAsync(It.IsAny<int>())).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Assert.ThrowsAsync<InvalidStateException>(() => Hub.InvitePlayer(invited_user_id));

        invitedUser.Verify(r => r.Invited(
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

        Database.Setup(d => d.GetUserAllowsPMs(invited_user_id)).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(invited_user_id);

        invitedUser.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            string.Empty
        ), Times.Once);
    }

    [Fact]
    public async Task UserCanInviteIntoRoomWithPassword()
    {
        const string password = "password";

        Database.Setup(db => db.GetRoomAsync(It.IsAny<long>()))
                .Callback<long>(InitialiseRoom)
                .ReturnsAsync(new multiplayer_room
                {
                    password = password,
                    user_id = USER_ID
                });

        SetUserContext(ContextUser);
        await Hub.JoinRoomWithPassword(ROOM_ID, password);

        Database.Setup(d => d.GetUserAllowsPMs(invited_user_id)).ReturnsAsync(true);

        SetUserContext(ContextUser);
        await Hub.InvitePlayer(invited_user_id);

        invitedUser.Verify(r => r.Invited(
            USER_ID,
            ROOM_ID,
            password
        ), Times.Once);
    }
}
