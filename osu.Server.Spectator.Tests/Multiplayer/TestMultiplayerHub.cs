// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TestMultiplayerHub : MultiplayerHub
    {
        public new MultiplayerHubContext HubContext => base.HubContext;

        public TestMultiplayerHub(
            ILoggerFactory loggerFactory,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            IDatabaseFactory databaseFactory,
            ChatFilters chatFilters,
            IHubContext<MultiplayerHub> hubContext,
            ISharedInterop sharedInterop,
            MultiplayerEventLogger multiplayerEventLogger,
            IMatchmakingQueueBackgroundService matchmakingQueueBackgroundService)
            : base(loggerFactory, rooms, users, databaseFactory, chatFilters, hubContext, sharedInterop, multiplayerEventLogger, matchmakingQueueBackgroundService)
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
