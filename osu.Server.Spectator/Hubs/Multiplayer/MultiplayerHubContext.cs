// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Entities;
using IDatabaseFactory = osu.Server.Spectator.Database.IDatabaseFactory;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Allows communication with multiplayer clients from potentially outside of a direct <see cref="MultiplayerHub"/> context.
    /// </summary>
    public class MultiplayerHubContext : IMultiplayerHubContext
    {
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger logger;

        public MultiplayerHubContext(
            MultiplayerEventDispatcher eventDispatcher,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory)
        {
            this.eventDispatcher = eventDispatcher;
            this.rooms = rooms;
            this.users = users;
            this.databaseFactory = databaseFactory;

            logger = loggerFactory.CreateLogger(nameof(MultiplayerHub).Replace("Hub", string.Empty));
        }

        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item, bool beatmapChanged)
        {
            if (item.ID == room.Settings.PlaylistItemId)
            {
                await room.EnsureAllUsersValidStyle();
                await room.UnreadyAllUsers(beatmapChanged);
            }

            await eventDispatcher.PostPlaylistItemChangedAsync(room.RoomID, item);
        }

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
        }

        public void Log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information)
        {
            logger.Log(logLevel, "[user:{userId}] [room:{roomID}] {message}",
                getLoggableUserIdentifier(user),
                room.RoomID,
                message.Trim());
        }

        public void Error(MultiplayerRoomUser? user, string message, Exception exception)
        {
            logger.LogError(exception, "[user:{userId}] {message}",
                getLoggableUserIdentifier(user),
                message.Trim());
        }

        private string getLoggableUserIdentifier(MultiplayerRoomUser? user)
        {
            return user?.UserID.ToString() ?? "???";
        }
    }
}
