// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.Caching.Distributed;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TestMultiplayerHub : MultiplayerHub
    {
        public TestMultiplayerHub(IDistributedCache cache, EntityStore<ServerMultiplayerRoom> rooms, EntityStore<MultiplayerClientState> users, IDatabaseFactory databaseFactory)
            : base(cache, rooms, users, databaseFactory)
        {
        }

        public new ItemUsage<ServerMultiplayerRoom> GetRoom(long roomId) => Rooms.GetForUse(roomId).Result;

        public bool CheckRoomExists(long roomId)
        {
            try
            {
                using (var usage = Rooms.GetForUse(roomId).Result)
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
