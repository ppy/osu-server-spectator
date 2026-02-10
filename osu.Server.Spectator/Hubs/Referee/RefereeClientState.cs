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

        public Task SubscribeToEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.SubscribeRefereeAsync(roomId, ConnectionId);

        public Task UnsubscribeFromEvents(MultiplayerEventDispatcher eventDispatcher, long roomId)
            => eventDispatcher.UnsubscribeRefereeAsync(roomId, ConnectionId);
    }
}
