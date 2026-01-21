// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerRoomController : IMultiplayerRoomController
    {
        private readonly EntityStore<ServerMultiplayerRoom> rooms;

        public MultiplayerRoomController(
            EntityStore<ServerMultiplayerRoom> rooms)
        {
            this.rooms = rooms;
        }

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
        }
    }
}
