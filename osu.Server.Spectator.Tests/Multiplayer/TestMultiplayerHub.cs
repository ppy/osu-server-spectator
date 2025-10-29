// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class TestMultiplayerHub : MultiplayerHub
    {
        public TestMultiplayerHub(
            ILoggerFactory loggerFactory,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            IDatabaseFactory databaseFactory,
            ChatFilters chatFilters,
            IMultiplayerHubContext hubContext,
            ISharedInterop sharedInterop,
            MultiplayerEventLogger multiplayerEventLogger,
            IMatchmakingQueueBackgroundService matchmakingQueueBackgroundService)
            : base(loggerFactory, rooms, users, databaseFactory, chatFilters, hubContext, sharedInterop, multiplayerEventLogger, matchmakingQueueBackgroundService)
        {
        }

        /// <summary>
        /// Joins or creates the room with a given ID.
        /// </summary>
        public new Task<MultiplayerRoom> JoinRoom(long roomId)
            => JoinRoomWithPassword(roomId, string.Empty);

        /// <summary>
        /// Joins or creates the room with a given ID.
        /// </summary>
        public new Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
        {
            if (CheckRoomExists(roomId))
                return base.JoinRoomWithPassword(roomId, password);

            return CreateRoom(new MultiplayerRoom(roomId) { Settings = { Password = password } });
        }

        /// <summary>
        /// Joins the room with a given ID.
        /// </summary>
        public Task<MultiplayerRoom> JoinRoomExplicit(long roomId)
            => base.JoinRoom(roomId);

        /// <summary>
        /// Joins the room with a given ID.
        /// </summary>
        public Task<MultiplayerRoom> JoinRoomWithPasswordExplicit(long roomId, string password)
            => base.JoinRoomWithPassword(roomId, password);

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
