// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Referee.Models.Responses;
using osu.Server.Spectator.Services;
using MatchType = osu.Game.Online.Rooms.MatchType;

namespace osu.Server.Spectator.Hubs.Referee
{
    [Authorize(ConfigureJwtBearerOptions.REFEREE_CLIENT_SCHEME)]
    public class RefereeHub : Hub, IRefereeHubServer
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly ISharedInterop sharedInterop;
        private readonly ILogger<RefereeHub> logger;
        private readonly IMultiplayerRoomController roomController;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly EntityStore<RefereeClientState> refereeStates;
        private readonly EntityStore<MultiplayerClientState> playerStates;

        public RefereeHub(
            IDatabaseFactory databaseFactory,
            ILoggerFactory loggerFactory,
            ISharedInterop sharedInterop,
            IMultiplayerRoomController roomController,
            MultiplayerEventDispatcher eventDispatcher,
            EntityStore<RefereeClientState> refereeStates,
            EntityStore<MultiplayerClientState> playerStates)
        {
            this.databaseFactory = databaseFactory;
            logger = loggerFactory.CreateLogger<RefereeHub>();
            this.sharedInterop = sharedInterop;
            this.roomController = roomController;
            this.eventDispatcher = eventDispatcher;
            this.refereeStates = refereeStates;
            this.playerStates = playerStates;
        }

        public override async Task OnConnectedAsync()
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId(), true))
            {
                // contrary to `StatefulUserHub`s which fully drop users' states on reconnection, we preserve the set of refereed rooms from previous connections, if any.
                // this is done to allow reconnections after an unclean connection drop-out.
                // note that this setup only supports at most ONE connection per user ID at any given time (this limit being independent of stateful hubs).
                // this is because of the singular `ConnectionId` and its use when subscribing to relevant groups.
                // if it becomes a problem, that can be pretty trivially remedied by tracking multiple `ConnectionId`s in `RefereeClientState` -
                // but note there will be no separation between `ConnectionId`s for a single user, so any connection associated with a single user will have the same permissions.
                userUsage.Item = new RefereeClientState(Context.ConnectionId, Context.GetUserId(), userUsage.Item?.RefereedRoomIds);
            }

            await base.OnConnectedAsync();
        }

        public async Task Ping(string message)
        {
            string? username;

            using (var db = databaseFactory.GetInstance())
                username = await db.GetUsernameAsync(Context.GetUserId());

            await Clients.Caller.SendAsync(nameof(IRefereeHubClient.Pong), $"Hi {username}! Here's your message back: {message}");
        }

        public async Task<RoomJoinedResponse> MakeRoom(int rulesetId, int beatmapId, string roomName)
        {
            log("Attempting to make room", null);

            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    ThrowHelper.ThrowUserRestricted();
            }

            var room = new MultiplayerRoom(new Room
            {
                Name = roomName,
                Password = Guid.NewGuid().ToString(),
                Type = MatchType.HeadToHead,
                QueueMode = QueueMode.HostOnly,
                AutoSkip = true,
                Playlist =
                [
                    new PlaylistItem(new APIBeatmap { OnlineID = beatmapId }).With(ruleset: rulesetId)
                ]
            });

            long roomId = await sharedInterop.CreateRoomAsync(Context.GetUserId(), room);
            await eventDispatcher.PostRoomCreatedAsync(roomId, Context.GetUserId());

            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                var createdRoom = await roomController.CreateRoom(userUsage.Item, roomId, room.Settings.Password);
                return new RoomJoinedResponse(createdRoom);
            }
        }

        public async Task<RoomJoinedResponse> JoinRoom(long roomId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    ThrowHelper.ThrowUserRestricted();
            }

            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                ensureIsReferee(roomId, userUsage);

                using (var roomUsage = await roomController.GetRoom(roomId))
                {
                    Debug.Assert(roomUsage.Item != null);

                    if (!await roomUsage.Item.UserCanJoin(userUsage.Item.UserId))
                        ThrowHelper.ThrowRoomNotJoinable();

                    var joinedRoom = await roomController.JoinRoom(userUsage.Item, roomId, roomUsage.Item.Settings.Password);
                    return new RoomJoinedResponse(joinedRoom);
                }
            }
        }

        public async Task LeaveRoom(long roomId)
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                ensureIsReferee(roomId, userUsage);

                using (var roomUsage = await roomController.GetRoom(roomId))
                {
                    Debug.Assert(roomUsage.Item != null);

                    await roomController.LeaveRoom(userUsage.Item, roomUsage);
                }
            }
        }

        public async Task CloseRoom(long roomId)
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                ensureIsReferee(roomId, userUsage);

                using (var roomUsage = await roomController.GetRoom(roomId))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        ThrowHelper.ThrowRoomDoesNotExist();

                    log("Closing room", room);

                    foreach (var user in room.Users.Where(u => u.UserID != userUsage.Item.UserId).ToArray())
                    {
                        switch (user.Role)
                        {
                            case MultiplayerRoomUserRole.Player:
                            {
                                using (var targetUserUsage = await playerStates.GetForUse(user.UserID))
                                {
                                    Debug.Assert(targetUserUsage.Item != null);

                                    if (!targetUserUsage.Item.IsAssociatedWithRoom(roomId))
                                        ThrowHelper.ThrowUserNotInRoom();

                                    await roomController.KickUserFromRoom(targetUserUsage.Item, roomUsage, userUsage.Item.UserId);
                                }

                                break;
                            }

                            case MultiplayerRoomUserRole.Referee:
                            {
                                using (var targetUserUsage = await refereeStates.GetForUse(user.UserID))
                                {
                                    Debug.Assert(targetUserUsage.Item != null);

                                    if (!targetUserUsage.Item.IsAssociatedWithRoom(roomId))
                                        ThrowHelper.ThrowUserNotInRoom();

                                    await roomController.KickUserFromRoom(targetUserUsage.Item, roomUsage, userUsage.Item.UserId);
                                }

                                break;
                            }
                        }
                    }

                    await roomController.LeaveRoom(userUsage.Item, roomUsage);
                }
            }
        }

        public async Task InvitePlayer(long roomId, int userId)
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                ensureIsReferee(roomId, userUsage);

                using (var roomUsage = await roomController.GetRoom(roomId))
                {
                    if (roomUsage.Item == null)
                        ThrowHelper.ThrowRoomDoesNotExist();

                    await roomUsage.Item.InvitePlayer(userId, invitedBy: userUsage.Item.UserId);
                }
            }
        }

        public async Task KickPlayer(long roomId, int userId)
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                Debug.Assert(userUsage.Item != null);

                ensureIsReferee(roomId, userUsage);

                using (var roomUsage = await roomController.GetRoom(roomId))
                {
                    if (roomUsage.Item == null)
                        ThrowHelper.ThrowRoomDoesNotExist();

                    var user = roomUsage.Item.Users.SingleOrDefault(u => u.UserID == userId);
                    if (user == null)
                        ThrowHelper.ThrowUserNotInRoom();

                    using (var targetUserUsage = await playerStates.GetForUse(user.UserID))
                    {
                        Debug.Assert(targetUserUsage.Item != null);
                        await roomController.KickUserFromRoom(targetUserUsage.Item, roomUsage, userUsage.Item.UserId);
                    }
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            using (var userUsage = await refereeStates.GetForUse(Context.GetUserId()))
            {
                if (userUsage.Item == null)
                {
                    await base.OnDisconnectedAsync(exception);
                    return;
                }

                if (exception != null)
                {
                    // if the disconnection isn't clean (due to a networking issue or similar), keep the user's set of refereed rooms intact
                    // so that they remain referees on all rooms when they reconnect.
                    // obviously, this is memory state, which is not preserved anywhere else, so it will drop out on server restart.
                    // additionally, we still unsubscribe the connection from events,
                    // primarily because only one connection per user ID is supported as written, so the user will need to re-subscribe with a new connection ID when they rejoin anyway.
                    foreach (long roomId in userUsage.Item.RefereedRoomIds)
                        await userUsage.Item.UnsubscribeFromEvents(eventDispatcher, roomId);

                    await base.OnDisconnectedAsync(exception);
                    return;
                }

                // if the disconnection is clean, it is treated as if the referee wishes to cease being a referee on all their rooms.
                // in line, perform a full leave.
                // this will remove the user from the set of referees on all their refereed rooms. the user will not get the referee status back on rejoin.
                foreach (long roomId in userUsage.Item.RefereedRoomIds.ToArray())
                {
                    using (var roomUsage = await roomController.GetRoom(roomId))
                        await roomController.LeaveRoom(userUsage.Item, roomUsage);
                }

                userUsage.Destroy();
            }

            await base.OnDisconnectedAsync(exception);
        }

        private static void ensureIsReferee(long roomId, ItemUsage<RefereeClientState> userUsage)
        {
            Debug.Assert(userUsage.Item != null);

            if (!userUsage.Item.IsAssociatedWithRoom(roomId))
                ThrowHelper.ThrowUserNotReferee();
        }

        private void log(string message, ServerMultiplayerRoom? room, LogLevel level = LogLevel.Information)
            => logger.Log(level, "[user:{userId}] [room:{roomId}] {message}", Context.UserIdentifier, room?.RoomID.ToString() ?? "none", message);
    }
}
