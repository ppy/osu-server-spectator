// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Game.Online.Multiplayer;
using StatsdClient;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    [Serializable]
    public class MultiplayerClientState : ClientState, IMultiplayerUserState
    {
        private static int countUsersInRooms;

        public long? CurrentRoomID { get; private set; }

        [JsonConstructor]
        public MultiplayerClientState(in string connectionId, in int userId)
            : base(connectionId, userId)
        {
        }

        int IMultiplayerUserState.UserId => UserId;

        public MultiplayerRoomUser CreateRoomUser()
            => new MultiplayerRoomUser(UserId) { Role = MultiplayerRoomUserRole.Player };

        public void AssociateWithRoom(long roomId)
        {
            if (CurrentRoomID != null)
                throw new InvalidOperationException("User is already in a room.");

            CurrentRoomID = roomId;
            DogStatsd.Gauge($"{MultiplayerHub.STATSD_PREFIX}.users", Interlocked.Increment(ref countUsersInRooms));
        }

        public bool IsAssociatedWithRoom(long roomId) => CurrentRoomID == roomId;

        public void DisassociateFromRoom(long roomId)
        {
            if (CurrentRoomID == null)
                return;

            CurrentRoomID = null;
            DogStatsd.Gauge($"{MultiplayerHub.STATSD_PREFIX}.users", Interlocked.Decrement(ref countUsersInRooms));
        }

        public Task SubscribeToEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.SubscribePlayerAsync(roomId, ConnectionId);

        public Task UnsubscribeFromEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.UnsubscribePlayerAsync(roomId, ConnectionId);
    }
}
