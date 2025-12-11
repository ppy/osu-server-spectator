// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using osu.Game.Online;
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

        private static readonly MessagePackSerializerOptions message_pack_options = new MessagePackSerializerOptions(new SignalRUnionWorkaroundResolver());

        protected readonly EntityStore<ServerMultiplayerRoom> Rooms;
        protected readonly IMultiplayerHubContext HubContext;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ChatFilters chatFilters;
        private readonly ISharedInterop sharedInterop;
        private readonly MultiplayerEventLogger multiplayerEventLogger;
        private readonly IMatchmakingQueueBackgroundService matchmakingQueueService;

        public MultiplayerHub(
            ILoggerFactory loggerFactory,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            IDatabaseFactory databaseFactory,
            ChatFilters chatFilters,
            IMultiplayerHubContext hubContext,
            ISharedInterop sharedInterop,
            MultiplayerEventLogger multiplayerEventLogger,
            IMatchmakingQueueBackgroundService matchmakingQueueService)
            : base(loggerFactory, users)
        {
            this.databaseFactory = databaseFactory;
            this.chatFilters = chatFilters;
            this.sharedInterop = sharedInterop;
            this.multiplayerEventLogger = multiplayerEventLogger;
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
            await multiplayerEventLogger.LogRoomCreatedAsync(roomId, Context.GetUserId());

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
            byte[] roomBytes;

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
                            room = roomUsage.Item ??= await ServerMultiplayerRoom.InitialiseAsync(roomId, HubContext, databaseFactory, multiplayerEventLogger);

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

                            // because match controllers may send subsequent information via Users collection hooks,
                            // inform clients before adding user to the room.
                            await Clients.Group(GetGroupId(roomId)).UserJoined(roomUser);

                            await room.AddUser(roomUser);
                            room.UpdateForRetrieval();

                            await addDatabaseUser(room, roomUser);
                            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));

                            Log(room, "User joined");
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

                        roomBytes = MessagePackSerializer.Serialize<MultiplayerRoom>(room, message_pack_options);
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

            await multiplayerEventLogger.LogPlayerJoinedAsync(roomId, Context.GetUserId());

            return MessagePackSerializer.Deserialize<MultiplayerRoom>(roomBytes, message_pack_options);
        }

        public async Task LeaveRoom()
        {
            Log("Requesting to leave room");
            long roomId;

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID == null)
                    return;

                roomId = userUsage.Item.CurrentRoomID.Value;
                await leaveRoom(userUsage.Item, false);
            }

            await multiplayerEventLogger.LogPlayerLeftAsync(roomId, Context.GetUserId());
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

                    await Clients.User(userId.ToString()).Invited(user.UserId, room.RoomID, room.Settings.Password);
                }
            }
        }

        public async Task TransferHost(int userId)
        {
            long roomId;

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    Log(room, $"Transferring host from {room.Host?.UserID} to {userId}");
                    roomId = room.RoomID;

                    ensureIsHost(room);

                    var newHost = room.Users.FirstOrDefault(u => u.UserID == userId);

                    if (newHost == null)
                        throw new Exception("Target user is not in the current room");

                    await setNewHost(room, newHost);
                }
            }

            await multiplayerEventLogger.LogHostChangedAsync(roomId, userId);
        }

        public async Task KickUser(int userId)
        {
            long roomId;

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    Log(room, $"Kicking user {userId}");
                    roomId = room.RoomID;

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

            await multiplayerEventLogger.LogPlayerKickedAsync(roomId, userId);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidStateException("Local user was not found in the expected room");

                    if (user.State == newState)
                        return;

                    // There's a potential that a client attempts to change state while a message from the server is in transit. Silently block these changes rather than informing the client.
                    switch (newState)
                    {
                        // If a client triggered `Idle` (ie. un-readying) before they received the `WaitingForLoad` message from the match starting.
                        case MultiplayerUserState.Idle:
                            if (IsGameplayState(user.State))
                                return;

                            break;

                        // If a client a triggered gameplay state before they received the `Idle` message from their gameplay being aborted.
                        case MultiplayerUserState.Loaded:
                        case MultiplayerUserState.ReadyForGameplay:
                            if (!IsGameplayState(user.State))
                                return;

                            break;
                    }

                    Log(room, $"User changing state from {user.State} to {newState}");

                    ensureValidStateSwitch(room, user.State, newState);

                    await HubContext.ChangeAndBroadcastUserState(room, user, newState);

                    // Signal newly-spectating users to load gameplay if currently in the middle of play.
                    if (newState == MultiplayerUserState.Spectating
                        && (room.State == MultiplayerRoomState.WaitingForLoad || room.State == MultiplayerRoomState.Playing))
                    {
                        await Clients.Caller.LoadRequested();
                    }

                    await HubContext.UpdateRoomStateIfRequired(room);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    await HubContext.ChangeAndBroadcastUserBeatmapAvailability(room, user, newBeatmapAvailability);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    await HubContext.ChangeUserStyle(beatmapId, rulesetId, room, user);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    await HubContext.ChangeUserMods(newMods, room, user);
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

                            await room.StartCountdown(new MatchStartCountdown { TimeRemaining = startMatchCountdownRequest.Duration }, HubContext.StartMatch);

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

                    await HubContext.StartMatch(room);
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

                    if (room.State != MultiplayerRoomState.WaitingForLoad && room.State != MultiplayerRoomState.Playing)
                        throw new InvalidStateException("Cannot abort a match that hasn't started.");

                    foreach (var user in room.Users)
                        await HubContext.ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);

                    await Clients.Group(GetGroupId(room.RoomID)).GameplayAborted(GameplayAbortReason.HostAbortedTheMatch);

                    await HubContext.UpdateRoomStateIfRequired(room);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    if (!IsGameplayState(user.State))
                        throw new InvalidStateException("Cannot abort gameplay while not in a gameplay state");

                    await HubContext.ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                    await HubContext.UpdateRoomStateIfRequired(room);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    if (!IsGameplayState(user.State))
                        throw new InvalidStateException("Cannot skip while not in a gameplay state");

                    await HubContext.ChangeUserVoteToSkipIntro(room, user, true);
                    await HubContext.CheckVotesToSkipPassed(room);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    Log(room, $"Adding playlist item for beatmap {item.BeatmapID}");
                    await room.Controller.AddPlaylistItem(item, user);

                    await HubContext.UpdateRoomStateIfRequired(room);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    Log(room, $"Editing playlist item {item.ID} for beatmap {item.BeatmapID}");
                    await room.Controller.EditPlaylistItem(item, user);
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

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    Log(room, $"Removing playlist item {playlistItemId}");
                    await room.Controller.RemovePlaylistItem(playlistItemId, user);

                    await HubContext.UpdateRoomStateIfRequired(room);
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

                    Log(room, "Settings updating");

                    settings.Name = await chatFilters.FilterAsync(settings.Name);

                    // Server is authoritative over the playlist item ID.
                    // Todo: This needs to change for tournament mode.
                    settings.PlaylistItemId = room.Settings.PlaylistItemId;

                    if (room.Settings.Equals(settings))
                        return;

                    var previousSettings = room.Settings;

                    if (settings.MatchType == MatchType.Playlists)
                        throw new InvalidStateException("Invalid match type selected");

                    try
                    {
                        room.Settings = settings;
                        await updateDatabaseSettings(room);
                    }
                    catch
                    {
                        // rollback settings if an error occurred when updating the database.
                        room.Settings = previousSettings;
                        throw;
                    }

                    if (previousSettings.MatchType != settings.MatchType)
                    {
                        await room.ChangeMatchType(settings.MatchType);
                        Log(room, $"Switching room ruleset to {room.Controller}");
                    }

                    await room.Controller.HandleSettingsChanged();
                    await HubContext.NotifySettingsChanged(room, false);

                    await HubContext.UpdateRoomStateIfRequired(room);
                }
            }
        }

        /// <summary>
        /// Get the group ID to be used for multiplayer messaging.
        /// </summary>
        /// <param name="roomId">The databased room ID.</param>
        public static string GetGroupId(long roomId) => $"room:{roomId}";

        private async Task updateDatabaseSettings(MultiplayerRoom room)
        {
            var playlistItem = room.Playlist.FirstOrDefault(item => item.ID == room.Settings.PlaylistItemId);

            if (playlistItem == null)
                throw new InvalidStateException("Attempted to select a playlist item not contained by the room.");

            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomSettingsAsync(room);
        }

        private async Task updateDatabaseHost(MultiplayerRoom room)
        {
            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomHostAsync(room);
        }

        private async Task endDatabaseMatch(MultiplayerRoom room)
        {
            using (var db = databaseFactory.GetInstance())
                await db.EndMatchAsync(room);

            await multiplayerEventLogger.LogRoomDisbandedAsync(room.RoomID, Context.GetUserId());
        }

        private async Task addDatabaseUser(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            using (var db = databaseFactory.GetInstance())
                await db.AddRoomParticipantAsync(room, user);
        }

        private async Task removeDatabaseUser(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            using (var db = databaseFactory.GetInstance())
                await db.RemoveRoomParticipantAsync(room, user);
        }

        protected override async Task CleanUpState(MultiplayerClientState state)
        {
            await base.CleanUpState(state);
            await matchmakingQueueService.RemoveFromQueueAsync(state);
            await leaveRoom(state, false);
        }

        private async Task setNewHost(MultiplayerRoom room, MultiplayerRoomUser newHost)
        {
            room.Host = newHost;
            await Clients.Group(GetGroupId(room.RoomID)).HostChanged(newHost.UserID);

            await updateDatabaseHost(room);
        }

        /// <summary>
        /// Given a room and a state transition, throw if there's an issue with the sequence of events.
        /// </summary>
        /// <param name="room">The room.</param>
        /// <param name="oldState">The old state.</param>
        /// <param name="newState">The new state.</param>
        private void ensureValidStateSwitch(ServerMultiplayerRoom room, MultiplayerUserState oldState, MultiplayerUserState newState)
        {
            switch (newState)
            {
                case MultiplayerUserState.Idle:
                    if (IsGameplayState(oldState))
                        throw new InvalidStateException("Cannot return to idle without aborting gameplay.");

                    // any non-gameplay state can return to idle.
                    break;

                case MultiplayerUserState.Ready:
                    if (oldState != MultiplayerUserState.Idle)
                        throw new InvalidStateChangeException(oldState, newState);

                    if (room.Controller.CurrentItem.Expired)
                        throw new InvalidStateException("Cannot ready up while all items have been played.");

                    break;

                case MultiplayerUserState.WaitingForLoad:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.Loaded:
                    if (oldState != MultiplayerUserState.WaitingForLoad)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.ReadyForGameplay:
                    if (oldState != MultiplayerUserState.Loaded)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.Playing:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.FinishedPlay:
                    if (oldState != MultiplayerUserState.Playing)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.Results:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.Spectating:
                    if (oldState != MultiplayerUserState.Idle && oldState != MultiplayerUserState.Ready)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }

        public static bool IsGameplayState(MultiplayerUserState state)
        {
            switch (state)
            {
                default:
                    return false;

                case MultiplayerUserState.WaitingForLoad:
                case MultiplayerUserState.Loaded:
                case MultiplayerUserState.ReadyForGameplay:
                case MultiplayerUserState.Playing:
                    return true;
            }
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

                Log(room, wasKick ? "User kicked" : "User left");

                await Groups.RemoveFromGroupAsync(state.ConnectionId, GetGroupId(room.RoomID));

                var user = room.Users.FirstOrDefault(u => u.UserID == state.UserId);

                if (user == null)
                    throw new InvalidStateException("User was not in the expected room.");

                await room.RemoveUser(user);
                await removeDatabaseUser(room, user);

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

                await HubContext.UpdateRoomStateIfRequired(room);

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    // there *has* to still be at least one user in the room (see user check above).
                    var newHost = room.Users.First();

                    await setNewHost(room, newHost);
                }

                if (wasKick)
                {
                    // the target user has already been removed from the group, so send the message to them separately.
                    await Clients.Client(state.ConnectionId).UserKicked(user);
                    await Clients.Group(GetGroupId(room.RoomID)).UserKicked(user);
                }
                else
                    await Clients.Group(GetGroupId(room.RoomID)).UserLeft(user);
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
