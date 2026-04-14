// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer;

namespace osu.Server.Spectator.Hubs.Referee
{
    public class RefereeClientState : ClientState, IMultiplayerUserState
    {
        private readonly HashSet<long> refereedRoomIds = [];
        public IEnumerable<long> RefereedRoomIds => refereedRoomIds;

        [JsonConstructor]
        public RefereeClientState(in string connectionId, in int userId, IEnumerable<long>? refereedRoomIds = null)
            : base(connectionId, userId)
        {
            if (refereedRoomIds != null)
                this.refereedRoomIds.UnionWith(refereedRoomIds);
        }

        int IMultiplayerUserState.UserId => UserId;

        public MultiplayerRoomUser CreateRoomUser()
            => new MultiplayerRoomUser(UserId) { Role = MultiplayerRoomUserRole.Referee };

        public void AssociateWithRoom(long roomId)
            => refereedRoomIds.Add(roomId);

        public bool IsAssociatedWithRoom(long roomId)
            => refereedRoomIds.Contains(roomId);

        public void DisassociateFromRoom(long roomId)
            => refereedRoomIds.Remove(roomId);

        public void DisassociateFromRooms(IEnumerable<long> roomIds)
            => refereedRoomIds.ExceptWith(roomIds);

        public void HandleRoomJoined(long roomId)
            => AssociateWithRoom(roomId);

        public void HandleRoomLeft(long roomId)
        {
            // intentionally a no-op.
            // a referee leaving a room intentionally has no real consequences.
            // the expectation is that it should NOT disassociate the referee from the room since they may want to return later for whatever reason.
            // the only way in which a referee CAN be disassociated from the room are as follows:
            // - getting removed as referee by another referee,
            // - closing the room entirely.
        }

        public Task SubscribeToEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.SubscribeRefereeAsync(roomId, ConnectionId);

        public Task UnsubscribeFromEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.UnsubscribeRefereeAsync(roomId, ConnectionId);
    }
}
