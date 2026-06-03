// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MaxParticipantsAndSlotsTests : MultiplayerTest
    {
        [Fact]
        public async Task MaxParticipantLimitEnforcement()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
            await checkSlots([1, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2]);

            SetUserContext(contextUser3);
            // expected to fail due to participant limit imposed.
            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.JoinRoom(ROOM_ID));

            SetUserContext(contextUser1);
            // lift participant limit.
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = null });
            await checkSlots(null);

            SetUserContext(contextUser3);
            // now expected to succeed.
            await Hub.JoinRoom(ROOM_ID);
        }

        [Fact]
        public async Task CannotSetParticipantLimitBelowCurrentCountOfUsersInRoom()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);

            await checkSlots(null);

            SetUserContext(contextUser1);
            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 }));
        }

        [Fact]
        public async Task SlotVacatedOnLeave()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
            await checkSlots([1, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2]);

            SetUserContext(contextUser1);
            await Hub.LeaveRoom();
            await checkSlots([null, 2]);
        }

        [Fact]
        public async Task SlotVacatedOnDisconnect()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 2 });
            await checkSlots([1, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2]);

            SetUserContext(contextUser1);
            await Hub.OnDisconnectedAsync(null);
            await checkSlots([null, 2]);
        }

        [Fact]
        public async Task SlotsAssignedInExistingRoom()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);

            await checkSlots(null);

            SetUserContext(contextUser1);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
            await checkSlots([1, 2, 3, null, null]);
        }

        [Fact]
        public async Task ManualSlotChanges()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
            await checkSlots([1, null, null, null, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, null, null, null]);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, 3, null, null]);

            SetUserContext(contextUser2);
            await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 });
            await checkSlots([1, null, 3, null, 2]);

            SetUserContext(contextUser3);
            // should fail due to requested slot being taken.
            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 0 }));

            // should fail due to out-of-range slot ID.
            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 10 }));
        }

        [Fact]
        public async Task ManualSlotChangesPreventedWhenRoomLocked()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
            await Hub.SendMatchRequest(new SetLockStateRequest { Locked = true });
            await checkSlots([1, null, null, null, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, null, null, null]);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, 3, null, null]);

            SetUserContext(contextUser2);
            // should fail due to room being locked.
            await Assert.ThrowsAsync<InvalidStateException>(async () => await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 }));
        }

        [Fact]
        public async Task AutomaticSlotReassignmentOnParticipantLimitChange()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 5 });
            await checkSlots([1, null, null, null, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, null, null, null]);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, 3, null, null]);

            SetUserContext(contextUser2);
            await Hub.SendMatchRequest(new ChangeSlotRequest { SlotID = 4 });
            await checkSlots([1, null, 3, null, 2]);

            SetUserContext(contextUser1);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 3 });
            await checkSlots([1, 2, 3]);

            await Hub.ChangeSettings(new MultiplayerRoomSettings { MaxParticipants = 8 });
            await checkSlots([1, 2, 3, null, null, null, null, null]);
        }

        [Fact]
        public async Task AutomaticSlotAssignmentInTeamVersus()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);
            CreateUser(4, out var contextUser4, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                MaxParticipants = 6,
                MatchType = MatchType.TeamVersus,
            });
            await checkSlots([1, null, null, null, null, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, null, null, 2, null, null]);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 3, null, 2, null, null]);

            SetUserContext(contextUser4);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 3, null, 2, 4, null]);
        }

        [Fact]
        public async Task SlotsNotChangedOnMatchTypeChange()
        {
            CreateUser(1, out var contextUser1, out _);
            CreateUser(2, out var contextUser2, out _);
            CreateUser(3, out var contextUser3, out _);
            CreateUser(4, out var contextUser4, out _);

            SetUserContext(contextUser1);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                MaxParticipants = 6,
            });
            await checkSlots([1, null, null, null, null, null]);

            SetUserContext(contextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, null, null, null, null]);

            SetUserContext(contextUser3);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, 3, null, null, null]);

            SetUserContext(contextUser4);
            await Hub.JoinRoom(ROOM_ID);
            await checkSlots([1, 2, 3, 4, null, null]);

            SetUserContext(contextUser1);
            await Hub.ChangeSettings(new MultiplayerRoomSettings
            {
                MaxParticipants = 8,
                MatchType = MatchType.TeamVersus,
            });
            await checkSlots([1, 2, 3, 4, null, null, null, null]);
        }

        private async Task checkSlots(int?[]? expected)
        {
            using (var usage = await Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equivalent(expected, (room.MatchState as StandardMatchRoomState)?.Slots);
            }
        }
    }
}
