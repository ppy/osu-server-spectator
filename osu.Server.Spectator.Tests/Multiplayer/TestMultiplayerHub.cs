// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TestMultiplayerHub : MultiplayerHub
    {
        public EntityStore<MultiplayerRoom> RoomStore => ACTIVE_ROOMS;
        public EntityStore<MultiplayerClientState> UserStore => ACTIVE_STATES;

        public TestMultiplayerHub(MemoryDistributedCache cache, IDatabaseFactory databaseFactory)
            : base(cache, databaseFactory)
        {
        }

        public ItemUsage<MultiplayerRoom> GetRoom(long roomId) => RoomStore.GetForUse(roomId).Result;

        public bool CheckRoomExists(long roomId)
        {
            try
            {
                using (var usage = RoomStore.GetForUse(roomId).Result)
                    return usage.Item != null;
            }
            catch
            {
                // probably not tracked.
                return false;
            }
        }
    }
}
