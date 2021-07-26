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
        public EntityStore<MultiplayerRoom> ActiveRooms { get; }
        public EntityStore<MultiplayerClientState> ActiveUsers { get; }

        public TestMultiplayerHub(MemoryDistributedCache cache, MultiplayerHubEntities entities, IDatabaseFactory databaseFactory)
            : base(cache, entities, databaseFactory)
        {
            ActiveUsers = entities.ActiveUsers;
            ActiveRooms = entities.ActiveRooms;
        }

        public ItemUsage<MultiplayerRoom> GetRoom(long roomId) => ActiveRooms.GetForUse(roomId).Result;

        public bool CheckRoomExists(long roomId)
        {
            try
            {
                using (var usage = ActiveRooms.GetForUse(roomId).Result)
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
