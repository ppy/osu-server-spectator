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

        /// <summary>
        /// The <see cref="MultiplayerRoom.Settings"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newSettings">The new settings of the room.</param>
        public async Task OnRoomSettingsChangedAsync(long roomId, MultiplayerRoomSettings newSettings)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), newSettings);
        }

        /// <summary>
        /// The <see cref="MultiplayerRoom.MatchState"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newMatchState">The new match state of the room.</param>
        public async Task OnMatchRoomStateChangedAsync(long roomId, MatchRoomState? newMatchState)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), newMatchState);
        }

        /// <summary>
        /// A <see cref="MatchServerEvent"/> has occurred in the room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="e">The relevant match event.</param>
        public async Task OnMatchEventAsync(long roomId, MatchServerEvent e)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        /// <summary>
        /// A user has joined the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who joined.</param>
        public async Task OnUserJoinedAsync(long roomId, MultiplayerRoomUser user)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserJoined), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_joined",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has left the given room on their own accord.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who left.</param>
        public async Task OnUserLeftAsync(long roomId, MultiplayerRoomUser user)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserLeft), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_left",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has been forcibly removed from the room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who was kicked.</param>
        public async Task OnUserKickedAsync(long roomId, MultiplayerRoomUser user)
        {
            // the target user has already been removed from the group, so send the message to them separately.
            await multiplayerHubContext.Clients.User(user.UserID.ToString()).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_kicked",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has been invited to join the given room.
        /// </summary>
        /// <param name="roomId">The ID of the room that the invitation pertains to.</param>
        /// <param name="invitedUserId">The ID of the user who was invited to the room.</param>
        /// <param name="invitedBy">The ID of the user who sent the invite.</param>
        /// <param name="password">The password to the given room.</param>
        public async Task OnUserInvitedAsync(long roomId, int invitedUserId, int invitedBy, string password)
        {
            await multiplayerHubContext.Clients.User(invitedUserId.ToString()).SendAsync(nameof(IMultiplayerClient.Invited), invitedBy, roomId, password);
        }

        /// <summary>
        /// The user with the given ID was made host of the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the user who was made host.</param>
        public async Task OnHostChangedAsync(long roomId, int userId)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.HostChanged), userId);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "host_changed",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// A user's state in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newUserState">The new state of the user in the room.</param>
        public async Task OnUserStateChangedAsync(long roomId, int userId, MultiplayerUserState newUserState)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserStateChanged), userId, newUserState);
        }

        /// <summary>
        /// A user's <see cref="MultiplayerRoomUser.MatchState"/> in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newMatchUserState">The new match state of the user in the room.</param>
        public async Task OnMatchUserStateChangedAsync(long roomId, int userId, MatchUserState? newMatchUserState)
        {
            await multiplayerHubContext.Clients.Group(MultiplayerHub.GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), userId, newMatchUserState);
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
