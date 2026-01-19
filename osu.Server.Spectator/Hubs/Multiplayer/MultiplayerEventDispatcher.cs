// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerEventDispatcher
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MultiplayerHub> multiplayerHubContext;
        private readonly ILogger<MultiplayerEventDispatcher> logger;

        public MultiplayerEventDispatcher(
            IDatabaseFactory databaseFactory,
            IHubContext<MultiplayerHub> multiplayerHubContext,
            ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.multiplayerHubContext = multiplayerHubContext;
            logger = loggerFactory.CreateLogger<MultiplayerEventDispatcher>();
        }

        /// <summary>
        /// Subscribes a connection with the given <paramref name="connectionId"/>
        /// to multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task SubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.AddToGroupAsync(connectionId, MultiplayerHub.GetGroupId(roomId));
        }

        /// <summary>
        /// Unsubscribes a connection with the given <paramref name="connectionId"/>
        /// from multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task UnsubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.RemoveFromGroupAsync(connectionId, MultiplayerHub.GetGroupId(roomId));
        }

        /// <summary>
        /// A new multiplayer room was created.
        /// </summary>
        /// <param name="roomId">The ID of the created room.</param>
        /// <param name="userId">The ID of the user that created the room.</param>
        public async Task OnRoomCreatedAsync(long roomId, int userId)
        {
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "room_created",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// A multiplayer room was disbanded.
        /// </summary>
        /// <param name="roomId">The ID of the disbanded room.</param>
        /// <param name="userId">The ID of the user that disbanded the room.</param>
        public async Task OnRoomDisbandedAsync(long roomId, int userId)
        {
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "room_disbanded",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// The <see cref="MultiplayerRoom.State"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newState">The new state of the room.</param>
        public async Task OnRoomStateChangedAsync(long roomId, MultiplayerRoomState newState)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.RoomStateChanged), newState);
        }

        private async Task logToDatabase(multiplayer_realtime_room_event ev)
        {
            try
            {
                using var db = databaseFactory.GetInstance();
                await db.LogRoomEventAsync(ev);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to log multiplayer room event to database");
            }
        }
    }
}
