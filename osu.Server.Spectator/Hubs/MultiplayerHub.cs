// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        // for the time being rooms will be maintained in memory and not distributed.
        private static readonly Dictionary<long, MultiplayerRoom> active_rooms = new Dictionary<long, MultiplayerRoom>();

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

            MultiplayerRoom? room;

            lock (active_rooms)
            {
                // check whether we are already aware of this match.

                if (!active_rooms.TryGetValue(roomId, out room))
                {
                    // TODO: get details of the room from the database. hard abort if non existent.
                    active_rooms.Add(roomId, room = new MultiplayerRoom());
                }
            }

            // add the user to the room.
            var user = room.Join(CurrentContextUserId);

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));
            await UpdateLocalUserState(new MultiplayerClientState(roomId));

            await Clients.Group(GetGroupId(roomId)).UserJoined(user);
            return true;
        }

        public async Task LeaveRoom(long roomId)
        {
            var state = await GetLocalUserState();

            if (state == null)
                return;

            MultiplayerRoom? room;

            lock (active_rooms)
            {
                if (!active_rooms.TryGetValue(roomId, out room))
                    return;
            }

            var user = room.Leave(CurrentContextUserId);

            if (user == null)
                return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(roomId));
            await RemoveLocalUserState();
            await Clients.Group(GetGroupId(roomId)).UserLeft(user);
        }

        public static string GetGroupId(long roomId) => $"room:{roomId}";
    }
}
