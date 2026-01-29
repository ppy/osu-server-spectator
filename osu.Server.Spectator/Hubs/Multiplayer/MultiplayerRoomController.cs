// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerRoomController : IMultiplayerRoomController
    {
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly IDatabaseFactory databaseFactory;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly ILoggerFactory loggerFactory;
        private readonly ISharedInterop sharedInterop;

        private readonly ILogger<MultiplayerRoomController> logger;

        public MultiplayerRoomController(
            EntityStore<ServerMultiplayerRoom> rooms,
            IDatabaseFactory databaseFactory,
            MultiplayerEventDispatcher eventDispatcher,
            ILoggerFactory loggerFactory,
            ISharedInterop sharedInterop)
        {
            this.rooms = rooms;
            this.databaseFactory = databaseFactory;
            this.eventDispatcher = eventDispatcher;
            this.loggerFactory = loggerFactory;
            this.sharedInterop = sharedInterop;

            logger = loggerFactory.CreateLogger<MultiplayerRoomController>();
        }

        public async Task<MultiplayerRoom> CreateRoom(ItemUsage<MultiplayerClientState> userUsage, long roomId, string password)
            => await joinOrCreateRoom(roomId, userUsage, password, isNewRoom: true);

        public async Task<MultiplayerRoom> JoinRoom(ItemUsage<MultiplayerClientState> userUsage, long roomId, string password)
            => await joinOrCreateRoom(roomId, userUsage, password, isNewRoom: false);

        private async Task<MultiplayerRoom> joinOrCreateRoom(long roomId, ItemUsage<MultiplayerClientState> userUsage, string password, bool isNewRoom)
        {
            Debug.Assert(userUsage.Item != null);

            MultiplayerRoom roomSnapshot;
            var roomUser = new MultiplayerRoomUser(userUsage.Item.UserId);

            try
            {
                using (var roomUsage = await rooms.GetForUse(roomId, isNewRoom))
                {
                    ServerMultiplayerRoom? room = null;

                    try
                    {
                        room = roomUsage.Item ??= await ServerMultiplayerRoom.InitialiseAsync(roomId, this, databaseFactory, eventDispatcher, loggerFactory);

                        // this is a sanity check to keep *rooms* in a good state.
                        // in theory the connection clean-up code should handle this correctly.
                        if (room.Users.Any(u => u.UserID == roomUser.UserID))
                            throw new InvalidOperationException($"User {roomUser.UserID} attempted to join room {room.RoomID} they are already present in.");

                        if (!await room.UserCanJoin(roomUser.UserID))
                            throw new InvalidStateException("Not eligible to join this room.");

                        if (!string.IsNullOrEmpty(room.Settings.Password))
                        {
                            if (room.Settings.Password != password)
                                throw new InvalidPasswordException();
                        }

                        if (isNewRoom && !room.Settings.MatchType.IsMatchmakingType())
                            room.Host = roomUser;

                        userUsage.Item.SetRoom(roomId);

                        await room.AddUser(roomUser);
                        await eventDispatcher.SubscribePlayerAsync(roomId, userUsage.Item.ConnectionId);

                        room.Log(roomUser, "User joined");
                    }
                    catch
                    {
                        try
                        {
                            if (userUsage.Item.CurrentRoomID != null)
                            {
                                // the user was joined to the room, so we can run the standard leaveRoom method.
                                // this will handle closing the room if this was the only user.
                                await removeUserFromRoom(userUsage.Item, roomUsage, userUsage.Item.UserId);
                            }
                            else if (isNewRoom)
                            {
                                if (room != null)
                                {
                                    // the room was retrieved and associated to the usage, but something failed before the user (host) could join.
                                    // for now, let's mark the room as ended if this happens.
                                    await endMatch(room, roomUser.UserID);
                                }

                                roomUsage.Destroy();
                            }
                        }
                        finally
                        {
                            // no matter how we end up cleaning up the room, ensure the user's state is cleared.
                            userUsage.Item.ClearRoom();
                        }

                        throw;
                    }

                    roomSnapshot = room.TakeSnapshot();
                }
            }
            catch (KeyNotFoundException)
            {
                logger.LogInformation("[user:{userId}] [room:{roomId}] Dropping attempt to join room before the host.", userUsage.Item.UserId, roomId);
                throw new InvalidStateException("Failed to join the room, please try again.");
            }

            try
            {
                // Run in background so we don't hold locks on user/room states.
                _ = sharedInterop.AddUserToRoomAsync(userUsage.Item.UserId, roomId, password);
            }
            catch
            {
                // Errors are logged internally by SharedInterop.
            }

            return roomSnapshot;
        }

        public async Task LeaveRoom(ItemUsage<MultiplayerClientState> userUsage, ItemUsage<ServerMultiplayerRoom> roomUsage)
        {
            Debug.Assert(userUsage.Item != null);
            await removeUserFromRoom(userUsage.Item, roomUsage, userUsage.Item.UserId);
        }

        public async Task KickUserFromRoom(ItemUsage<MultiplayerClientState> kickedUserUsage, ItemUsage<ServerMultiplayerRoom> roomUsage, int kickedBy)
        {
            Debug.Assert(kickedUserUsage.Item != null);
            await removeUserFromRoom(kickedUserUsage.Item, roomUsage, kickedBy);
        }

        private async Task removeUserFromRoom(MultiplayerClientState state, ItemUsage<ServerMultiplayerRoom> roomUsage, int removingUserId)
        {
            try
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                await eventDispatcher.UnsubscribePlayerAsync(room.RoomID, state.ConnectionId);

                var user = await room.RemoveUser(state.UserId);
                bool wasKick = removingUserId != user.UserID;
                room.Log(user, wasKick ? "User kicked" : "User left");

                try
                {
                    // Run in background so we don't hold locks on user/room states.
                    _ = sharedInterop.RemoveUserFromRoomAsync(state.UserId, room.RoomID);
                }
                catch
                {
                    // Errors are logged internally by SharedInterop.
                }

                // handle closing the room if the only participant is the user which is leaving.
                if (room.Users.Count == 0)
                {
                    await endMatch(room, removingUserId);

                    // only destroy the usage after the database operation succeeds.
                    room.Log("Stopping tracking of room (all users left).");
                    roomUsage.Destroy();
                    return;
                }

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    // there *has* to still be at least one user in the room (see user check above).
                    var newHost = room.Users.First();

                    await room.SetHost(newHost.UserID);
                }

                if (wasKick)
                    await eventDispatcher.PostUserKickedAsync(room.RoomID, user);
                else
                    await eventDispatcher.PostUserLeftAsync(room.RoomID, user);
            }
            finally
            {
                state.ClearRoom();
            }
        }

        private async Task endMatch(MultiplayerRoom room, int disbandingUserId)
        {
            using (var db = databaseFactory.GetInstance())
                await db.EndMatchAsync(room);

            await eventDispatcher.PostRoomDisbandedAsync(room.RoomID, disbandingUserId);
        }

        public Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId)
            => rooms.GetForUse(roomId);

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
        }
    }
}
