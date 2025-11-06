// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Moq;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingQueueBackgroundServiceTests : MultiplayerTest
    {
        public MatchmakingQueueBackgroundServiceTests()
        {
            Database.Setup(db => db.GetMatchmakingPoolAsync(It.IsAny<int>()))
                    .Returns<int>(id => Task.FromResult<matchmaking_pool?>(new matchmaking_pool
                    {
                        id = id,
                        name = $"pool-{id}",
                        active = true,
                        lobby_size = 2,
                    }));

            Database.Setup(db => db.GetRealtimeRoomAsync(0))
                    .Callback<long>(roomId => InitialiseRoom(roomId, 10))
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.matchmaking,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!),
                    });
        }

        [Fact]
        public async Task AddToQueue()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingQueueJoined(), Times.Once);
            UserReceiver.Verify(u => u.MatchmakingQueueLeft(), Times.Never);
        }

        [Fact]
        public async Task RemoveFromQueue()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.RemoveFromQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingQueueJoined(), Times.Once);
            UserReceiver.Verify(u => u.MatchmakingQueueLeft(), Times.Once);
        }

        [Fact]
        public async Task MatchReady()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Never);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Never);

            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
        }

        [Fact]
        public async Task AcceptInvitation()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);

            await MatchmakingBackgroundService.AcceptInvitationAsync(UserStates.GetEntityUnsafe(USER_ID)!);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            User2Receiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);

            await MatchmakingBackgroundService.AcceptInvitationAsync(UserStates.GetEntityUnsafe(USER_ID_2)!);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeclineInvitation()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);

            await MatchmakingBackgroundService.AcceptInvitationAsync(UserStates.GetEntityUnsafe(USER_ID)!);
            await MatchmakingBackgroundService.DeclineInvitationAsync(UserStates.GetEntityUnsafe(USER_ID_2)!);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            UserReceiver.Verify(u => u.MatchmakingQueueJoined(), Times.Exactly(2));
            UserReceiver.Verify(u => u.MatchmakingQueueLeft(), Times.Never);

            User2Receiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            User2Receiver.Verify(u => u.MatchmakingQueueJoined(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingQueueLeft(), Times.Once);
        }

        [Fact]
        public async Task LeaveQueueAfterInvite()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);

            await MatchmakingBackgroundService.AcceptInvitationAsync(UserStates.GetEntityUnsafe(USER_ID)!);
            await MatchmakingBackgroundService.RemoveFromQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            // Should be the same as DeclineInvitation()
            UserReceiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            UserReceiver.Verify(u => u.MatchmakingQueueJoined(), Times.Exactly(2));
            UserReceiver.Verify(u => u.MatchmakingQueueLeft(), Times.Never);

            // Should be the same as DeclineInvitation()
            User2Receiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            User2Receiver.Verify(u => u.MatchmakingQueueJoined(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingQueueLeft(), Times.Once);
        }

        [Fact]
        public async Task QueueLeftOnDisconnect()
        {
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID)!, 1);
            await MatchmakingBackgroundService.AddToQueueAsync(UserStates.GetEntityUnsafe(USER_ID_2)!, 1);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            UserReceiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingRoomInvited(), Times.Once);

            await MatchmakingBackgroundService.AcceptInvitationAsync(UserStates.GetEntityUnsafe(USER_ID)!);
            SetUserContext(ContextUser2);
            await Hub.OnDisconnectedAsync(null);
            await MatchmakingBackgroundService.ExecuteOnceAsync();

            // Should be the same as DeclineInvitation()
            UserReceiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            UserReceiver.Verify(u => u.MatchmakingQueueJoined(), Times.Exactly(2));
            UserReceiver.Verify(u => u.MatchmakingQueueLeft(), Times.Never);

            // Should be the same as DeclineInvitation()
            User2Receiver.Verify(u => u.MatchmakingRoomReady(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            User2Receiver.Verify(u => u.MatchmakingQueueJoined(), Times.Once);
            User2Receiver.Verify(u => u.MatchmakingQueueLeft(), Times.Once);
        }
    }
}
