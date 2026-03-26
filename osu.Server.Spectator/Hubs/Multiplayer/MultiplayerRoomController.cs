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
using osu.Server.Spectator.Hubs.Referee;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerRoomController : IMultiplayerRoomController
    {
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> players;
        private readonly EntityStore<RefereeClientState> referees;
        private readonly IDatabaseFactory databaseFactory;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly ILoggerFactory loggerFactory;
        private readonly ISharedInterop sharedInterop;

        private readonly ILogger<MultiplayerRoomController> logger;

        public MultiplayerRoomController(
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> players,
            EntityStore<RefereeClientState> referees,
            IDatabaseFactory databaseFactory,
            MultiplayerEventDispatcher eventDispatcher,
            ILoggerFactory loggerFactory,
            ISharedInterop sharedInterop)
        {
            this.rooms = rooms;
            this.players = players;
            this.referees = referees;
            this.databaseFactory = databaseFactory;
            this.eventDispatcher = eventDispatcher;
            this.loggerFactory = loggerFactory;
            this.sharedInterop = sharedInterop;

            logger = loggerFactory.CreateLogger<MultiplayerRoomController>();
        }

        public async Task<MultiplayerRoom> CreateRoom(IMultiplayerUserState user, long roomId, string password)
        {
            return await joinOrCreateRoom(roomId, user, password, isNewRoom: true);
        }

        public async Task<MultiplayerRoom> JoinRoom(IMultiplayerUserState user, long roomId, string password)
        {
            return await joinOrCreateRoom(roomId, user, password, isNewRoom: false);
        }

        private async Task<MultiplayerRoom> joinOrCreateRoom(long roomId, IMultiplayerUserState userState, string password, bool isNewRoom)
        {
            MultiplayerRoom roomSnapshot;
            var roomUser = userState.CreateRoomUser();

            try
            {
                using (var roomUsage = await rooms.GetForUse(roomId, isNewRoom))
                {
                    ServerMultiplayerRoom? room = null;

                    try
                    {
                        room = roomUsage.Item ??= await ServerMultiplayerRoom.InitialiseAsync(roomId, this, databaseFactory, eventDispatcher, loggerFactory);

                        if (!await room.UserCanJoin(roomUser.UserID))
                            throw new InvalidStateException("Not eligible to join this room.");

                        if (!string.IsNullOrEmpty(room.Settings.Password))
                        {
                            if (room.Settings.Password != password)
                                throw new InvalidPasswordException();
                        }

                        if (isNewRoom && !room.Settings.MatchType.IsMatchmakingType())
                            room.Host = roomUser;

                        userState.AssociateWithRoom(roomId);

                        var existingUser = room.Users.FirstOrDefault(u => u.UserID == roomUser.UserID);

                        if (existingUser == null)
                            await room.AddUser(roomUser);
                        else if (!isRefereeSpectatingOwnMatch(userState, existingUser))
                            throw new InvalidOperationException($"User {roomUser.UserID} attempted to join room {room.RoomID} they are already present in.");

                        await userState.SubscribeToEvents(eventDispatcher, roomId);

                        room.Log(roomUser, "User joined");
                    }
                    catch
                    {
                        try
                        {
                            if (userState.IsAssociatedWithRoom(roomId))
                            {
                                // the user was joined to the room, so we can run the standard leaveRoom method.
                                // this will handle closing the room if this was the only user.
                                await removeUserFromRoom(userState, roomUsage, userState.UserId);
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
                            userState.DisassociateFromRoom(roomId);
                        }

                        throw;
                    }

                    roomSnapshot = room.TakeSnapshot();
                }
            }
            catch (KeyNotFoundException)
            {
                logger.LogInformation("[user:{userId}] [room:{roomId}] Dropping attempt to join room before the host.", userState.UserId, roomId);
                throw new InvalidStateException("Failed to join the room, please try again.");
            }

            try
            {
                // Run in background so we don't hold locks on user/room states.
                _ = sharedInterop.AddUserToRoomAsync(userState.UserId, roomId, password);
            }
            catch
            {
                // Errors are logged internally by SharedInterop.
            }

            return roomSnapshot;
        }

        public async Task LeaveRoom(IMultiplayerUserState user, ItemUsage<ServerMultiplayerRoom> roomUsage)
            => await removeUserFromRoom(user, roomUsage, user.UserId);

        public async Task KickUserFromRoom(IMultiplayerUserState kickedUser, ItemUsage<ServerMultiplayerRoom> roomUsage, int kickedBy)
            => await removeUserFromRoom(kickedUser, roomUsage, kickedBy);

        public async Task BanUserFromRoom(int bannedUserId, ItemUsage<ServerMultiplayerRoom> roomUsage, int bannedBy)
        {
            if (bannedUserId == bannedBy)
                throw new InvalidStateException("User can't ban self.");

            var room = roomUsage.Item;
            if (room == null)
                throw new InvalidOperationException("Attempted to operate on a null room");

            var userInRoom = room.Users.FirstOrDefault(u => u.UserID == bannedUserId);

            if (userInRoom != null)
            {
                switch (userInRoom.Role)
                {
                    case MultiplayerRoomUserRole.Player:
                        using (var targetPlayerUsage = await players.GetForUse(bannedUserId))
                        {
                            Debug.Assert(targetPlayerUsage.Item != null);
                            await removeUserFromRoom(targetPlayerUsage.Item, roomUsage, bannedBy);
                        }

                        break;

                    case MultiplayerRoomUserRole.Referee:
                        using (var targetRefereeUsage = await referees.GetForUse(bannedUserId))
                        {
                            Debug.Assert(targetRefereeUsage.Item != null);
                            await removeUserFromRoom(targetRefereeUsage.Item, roomUsage, bannedBy);
                        }

                        break;
                }
            }

            room.BanUser(bannedUserId);
        }

        private async Task removeUserFromRoom(IMultiplayerUserState state, ItemUsage<ServerMultiplayerRoom> roomUsage, int removingUserId)
        {
            long? roomId = null;

            try
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                roomId = room.RoomID;

                await state.UnsubscribeFromEvents(eventDispatcher, room.RoomID);

                var user = room.Users.FirstOrDefault(u => u.UserID == state.UserId);
                if (user == null)
                    throw new InvalidStateException("User is not in the room.");

                if (isRefereeSpectatingOwnMatch(state, user))
                    return;

                await room.RemoveUser(state.UserId);

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
                    await eventDispatcher.PostUserKickedAsync(room.RoomID, user, removingUserId);
                else
                    await eventDispatcher.PostUserLeftAsync(room.RoomID, user);
            }
            finally
            {
                if (roomId != null)
                    state.DisassociateFromRoom(roomId.Value);
            }
        }

        /// <remarks>
        /// We want to allow exceptions wherein existing referees can also join their refereed rooms via the client to spectate them.
        /// In that case, hook up all associations but don't add/remove the user in the room model.
        /// Notably this flow requires the user to join as referee <i>first</i> and as spectator in client <i>second</i>.
        /// </remarks>
        private static bool isRefereeSpectatingOwnMatch(IMultiplayerUserState state, MultiplayerRoomUser user)
            => user.Role == MultiplayerRoomUserRole.Referee && state is MultiplayerClientState;

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
