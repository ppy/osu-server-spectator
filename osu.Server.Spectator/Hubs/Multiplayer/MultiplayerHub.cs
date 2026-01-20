// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        public const string STATSD_PREFIX = "multiplayer";

        protected readonly EntityStore<ServerMultiplayerRoom> Rooms;
        protected readonly IMultiplayerHubContext HubContext;
        private readonly ILoggerFactory loggerFactory;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ChatFilters chatFilters;
        private readonly ISharedInterop sharedInterop;
        private readonly MultiplayerEventDispatcher multiplayerEventDispatcher;
        private readonly IMatchmakingQueueBackgroundService matchmakingQueueService;

        public MultiplayerHub(
            ILoggerFactory loggerFactory,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            IDatabaseFactory databaseFactory,
            ChatFilters chatFilters,
            IMultiplayerHubContext hubContext,
            ISharedInterop sharedInterop,
            MultiplayerEventDispatcher multiplayerEventDispatcher,
            IMatchmakingQueueBackgroundService matchmakingQueueService)
            : base(loggerFactory, users)
        {
            this.loggerFactory = loggerFactory;
            this.databaseFactory = databaseFactory;
            this.chatFilters = chatFilters;
            this.sharedInterop = sharedInterop;
            this.multiplayerEventDispatcher = multiplayerEventDispatcher;
            this.matchmakingQueueService = matchmakingQueueService;

            Rooms = rooms;
            HubContext = hubContext;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            using (var usage = await GetOrCreateLocalUserState())
                usage.Item = new MultiplayerClientState(Context.ConnectionId, Context.GetUserId());
        }

        public async Task<MultiplayerRoom> CreateRoom(MultiplayerRoom room)
        {
            Log("Attempting to create room");

            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't join a room when restricted.");
            }

            long roomId = await sharedInterop.CreateRoomAsync(Context.GetUserId(), room);
            await multiplayerEventDispatcher.PostRoomCreatedAsync(roomId, Context.GetUserId());

            return await joinOrCreateRoom(roomId, room.Settings.Password, true);
        }

        public Task<MultiplayerRoom> JoinRoom(long roomId) => JoinRoomWithPassword(roomId, string.Empty);

        public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
        {
            Log($"Attempting to join room {roomId}");

            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't join a room when restricted.");
            }

            return await joinOrCreateRoom(roomId, password, false);
        }

        private async Task<MultiplayerRoom> joinOrCreateRoom(long roomId, string password, bool isNewRoom)
        {
            MultiplayerRoom roomSnapshot;

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID != null)
                    throw new InvalidStateException("Can't join a room when already in another room.");

                var roomUser = new MultiplayerRoomUser(Context.GetUserId());

                try
                {
                    using (var roomUsage = await Rooms.GetForUse(roomId, isNewRoom))
                    {
                        ServerMultiplayerRoom? room = null;

                        try
                        {
                            room = roomUsage.Item ??= await ServerMultiplayerRoom.InitialiseAsync(roomId, HubContext, databaseFactory, multiplayerEventDispatcher, loggerFactory);

                            // this is a sanity check to keep *rooms* in a good state.
                            // in theory the connection clean-up code should handle this correctly.
                            if (room.Users.Any(u => u.UserID == roomUser.UserID))
                                throw new InvalidOperationException($"User {roomUser.UserID} attempted to join room {room.RoomID} they are already present in.");

                            if (!await room.Controller.UserCanJoin(roomUser.UserID))
                                throw new InvalidStateException("Not eligible to join this room.");

                            if (!string.IsNullOrEmpty(room.Settings.Password))
                            {
                                if (room.Settings.Password != password)
                                    throw new InvalidPasswordException();
                            }

                            if (isNewRoom && room.Settings.MatchType != MatchType.Matchmaking)
                                room.Host = roomUser;

                            userUsage.Item.SetRoom(roomId);

                            await room.AddUser(roomUser);
                            await multiplayerEventDispatcher.SubscribePlayerAsync(roomId, Context.ConnectionId);

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
                                    await leaveRoom(userUsage.Item, roomUsage, false);
                                }
                                else if (isNewRoom)
                                {
                                    if (room != null)
                                    {
                                        // the room was retrieved and associated to the usage, but something failed before the user (host) could join.
                                        // for now, let's mark the room as ended if this happens.
                                        await endDatabaseMatch(room);
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
                    Log("Dropping attempt to join room before the host.", LogLevel.Error);
                    throw new InvalidStateException("Failed to join the room, please try again.");
                }
            }

            try
            {
                // Run in background so we don't hold locks on user/room states.
                _ = sharedInterop.AddUserToRoomAsync(Context.GetUserId(), roomId, password);
            }
            catch
            {
                // Errors are logged internally by SharedInterop.
            }

            return roomSnapshot;
        }

        public async Task LeaveRoom()
        {
            Log("Requesting to leave room");

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID == null)
                    return;

                await leaveRoom(userUsage.Item, false);
            }
        }

        public async Task InvitePlayer(int userId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                bool isRestricted = await db.IsUserRestrictedAsync(userId);
                if (isRestricted)
                    throw new InvalidStateException("Can't invite a restricted user to a room.");

                var relation = await db.GetUserRelation(Context.GetUserId(), userId);

                // The local user has the player they are trying to invite blocked.
                if (relation?.foe == true)
                    throw new UserBlockedException();

                var inverseRelation = await db.GetUserRelation(userId, Context.GetUserId());

                // The player being invited has the local user blocked.
                if (inverseRelation?.foe == true)
                    throw new UserBlockedException();

                // The player being invited disallows unsolicited PMs and the local user is not their friend.
                if (inverseRelation?.friend != true && !await db.GetUserAllowsPMs(userId))
                    throw new UserBlocksPMsException();
            }

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var user = userUsage.Item;
                    var room = roomUsage.Item;

                    if (user == null)
                        throw new InvalidStateException("Local user was not found in the expected room");

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    if (room.Settings.MatchType == MatchType.Matchmaking)
                        throw new InvalidStateException("Can't invite players to matchmaking rooms.");

                    await multiplayerEventDispatcher.PostUserInvitedAsync(room.RoomID, invitedUserId: userId, invitedBy: user.UserId, room.Settings.Password);
                }
            }
        }

        public async Task TransferHost(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    room.Log($"Transferring host from {room.Host?.UserID} to {userId}");

                    ensureIsHost(room);

                    await room.SetHost(userId);
                }
            }
        }

        public async Task KickUser(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    Log(room, $"Kicking user {userId}");

                    if (userId == userUsage.Item?.UserId)
                        throw new InvalidStateException("Can't kick self");

                    ensureIsHost(room);

                    var kickTarget = room.Users.FirstOrDefault(u => u.UserID == userId);

                    if (kickTarget == null)
                        throw new InvalidOperationException("Target user is not in the current room");

                    using (var targetUserUsage = await GetStateFromUser(kickTarget.UserID))
                    {
                        Debug.Assert(targetUserUsage.Item != null);

                        if (targetUserUsage.Item.CurrentRoomID == null)
                            throw new InvalidOperationException();

                        await leaveRoom(targetUserUsage.Item, roomUsage, true);
                    }
                }
            }
        }

        public async Task ChangeState(MultiplayerUserState newState)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserState(Context.GetUserId(), newState);
                }
            }
        }

        public async Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserBeatmapAvailability(Context.GetUserId(), newBeatmapAvailability);
                }
            }
        }

        public async Task ChangeUserStyle(int? beatmapId, int? rulesetId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserStyle(Context.GetUserId(), beatmapId, rulesetId);
                }
            }
        }

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserMods(Context.GetUserId(), newMods);
                }
            }
        }

        public async Task SendMatchRequest(MatchUserRequest request)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    switch (request)
                    {
                        case StartMatchCountdownRequest startMatchCountdownRequest:
                            ensureIsHost(room);

                            if (room.State != MultiplayerRoomState.Open)
                                throw new InvalidStateException("Cannot start a countdown during ongoing play.");

                            if (room.Settings.AutoStartEnabled)
                                throw new InvalidStateException("Cannot start manual countdown if auto-start is enabled.");

                            await room.StartCountdown(new MatchStartCountdown { TimeRemaining = startMatchCountdownRequest.Duration }, ServerMultiplayerRoom.StartMatch);

                            break;

                        case StopCountdownRequest stopCountdownRequest:
                            ensureIsHost(room);

                            MultiplayerCountdown? countdown = room.FindCountdownById(stopCountdownRequest.ID);

                            if (countdown == null)
                                break;

                            switch (countdown)
                            {
                                case MatchStartCountdown when room.Settings.AutoStartEnabled:
                                case ForceGameplayStartCountdown:
                                case ServerShuttingDownCountdown:
                                    throw new InvalidStateException("Cannot stop the requested countdown.");
                            }

                            await room.StopCountdown(countdown);
                            break;

                        default:
                            await room.Controller.HandleUserRequest(user, request);
                            break;
                    }
                }
            }
        }

        public async Task StartMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    ensureIsHost(room);

                    if (room.Host != null && room.Host.State != MultiplayerUserState.Spectating && room.Host.State != MultiplayerUserState.Ready)
                        throw new InvalidStateException("Can't start match when the host is not ready.");

                    if (room.Users.All(u => u.State != MultiplayerUserState.Ready))
                        throw new InvalidStateException("Can't start match when no users are ready.");

                    await ServerMultiplayerRoom.StartMatch(room);
                }
            }
        }

        public async Task AbortMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    ensureIsHost(room);

                    await room.AbortMatch();
                }
            }
        }

        public async Task AbortGameplay()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.AbortGameplay(Context.GetUserId());
                }
            }
        }

        public async Task VoteToSkipIntro()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.VoteToSkipIntro(Context.GetUserId());
                }
            }
        }

        public async Task AddPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.AddPlaylistItem(Context.GetUserId(), item);
                }
            }
        }

        public async Task EditPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.EditPlaylistItem(Context.GetUserId(), item);
                }
            }
        }

        public async Task RemovePlaylistItem(long playlistItemId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.RemovePlaylistItem(Context.GetUserId(), playlistItemId);
                }
            }
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    if (room.State != MultiplayerRoomState.Open)
                        throw new InvalidStateException("Attempted to change settings while game is active");

                    ensureIsHost(room);

                    settings.Name = await chatFilters.FilterAsync(settings.Name);
                    await room.ChangeRoomSettings(settings);
                }
            }
        }

        private async Task endDatabaseMatch(MultiplayerRoom room)
        {
            using (var db = databaseFactory.GetInstance())
                await db.EndMatchAsync(room);

            await multiplayerEventDispatcher.PostRoomDisbandedAsync(room.RoomID, Context.GetUserId());
        }

        protected override async Task CleanUpState(MultiplayerClientState state)
        {
            await base.CleanUpState(state);
            await matchmakingQueueService.RemoveFromQueueAsync(state);
            await leaveRoom(state, false);
        }

        /// <summary>
        /// Ensure the local user is the host of the room, and throw if they are not.
        /// </summary>
        private void ensureIsHost(MultiplayerRoom room)
        {
            if (room.Host?.UserID != Context.GetUserId())
                throw new NotHostException();
        }

        /// <summary>
        /// Retrieve the <see cref="MultiplayerRoom"/> for the local context user.
        /// </summary>
        private async Task<ItemUsage<ServerMultiplayerRoom>> getLocalUserRoom(MultiplayerClientState state)
        {
            if (state.CurrentRoomID == null)
                throw new NotJoinedRoomException();

            return await Rooms.GetForUse(state.CurrentRoomID.Value);
        }

        private async Task leaveRoom(MultiplayerClientState state, bool wasKick)
        {
            if (state.CurrentRoomID == null)
                return;

            using (var roomUsage = await getLocalUserRoom(state))
                await leaveRoom(state, roomUsage, wasKick);
        }

        private async Task leaveRoom(MultiplayerClientState state, ItemUsage<ServerMultiplayerRoom> roomUsage, bool wasKick)
        {
            if (state.CurrentRoomID == null)
                return;

            try
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                await multiplayerEventDispatcher.UnsubscribePlayerAsync(room.RoomID, state.ConnectionId);

                var user = await room.RemoveUser(state.UserId);
                room.Log(user, wasKick ? "User kicked" : "User left");

                try
                {
                    // Run in background so we don't hold locks on user/room states.
                    _ = sharedInterop.RemoveUserFromRoomAsync(state.UserId, state.CurrentRoomID.Value);
                }
                catch
                {
                    // Errors are logged internally by SharedInterop.
                }

                // handle closing the room if the only participant is the user which is leaving.
                if (room.Users.Count == 0)
                {
                    await endDatabaseMatch(room);

                    // only destroy the usage after the database operation succeeds.
                    Log(room, "Stopping tracking of room (all users left).");
                    roomUsage.Destroy();
                    return;
                }

                await room.UpdateRoomStateIfRequired();

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    // there *has* to still be at least one user in the room (see user check above).
                    var newHost = room.Users.First();

                    await room.SetHost(newHost.UserID);
                }

                if (wasKick)
                    await multiplayerEventDispatcher.PostUserKickedAsync(room.RoomID, user);
                else
                    await multiplayerEventDispatcher.PostUserLeftAsync(room.RoomID, user);
            }
            finally
            {
                state.ClearRoom();
            }
        }

        internal Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId) => Rooms.GetForUse(roomId);

        protected void Log(ServerMultiplayerRoom room, string message, LogLevel logLevel = LogLevel.Information) => base.Log($"[room:{room.RoomID}] {message}", logLevel);
    }
}
