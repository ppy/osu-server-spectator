// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        public MultiplayerHub([NotNull] IDistributedCache cache)
            : base(cache)
        {
        }

        public async Task<bool> JoinRoom(long roomId)
        {
            var state = await GetLocalUserState();

            if (state != null)
            {
                // if the user already has a state, it means they are already in a room and can't join another without first leaving.
                return false;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));
            await UpdateLocalUserState(new MultiplayerClientState(roomId));

            // todo: let others in the room know that this user joined.

            return true;
        }

        public async Task LeaveRoom(long roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(roomId));
            await RemoveLocalUserState();
        }

        public static string GetGroupId(long roomId) => $"room:{roomId}";
    }
}
