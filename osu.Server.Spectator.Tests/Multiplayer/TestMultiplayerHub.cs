// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Multiplayer;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TestMultiplayerHub : MultiplayerHub
    {
        public new MultiplayerHubContext HubContext => base.HubContext;

        public TestMultiplayerHub(IDistributedCache cache,
                                  EntityStore<ServerMultiplayerRoom> rooms,
                                  EntityStore<MultiplayerClientState> users,
                                  IDatabaseFactory databaseFactory,
                                  ChatFilters chatFilters,
                                  IHubContext<MultiplayerHub> hubContext)
            : base(cache, rooms, users, databaseFactory, chatFilters, hubContext)
        {
        }

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
