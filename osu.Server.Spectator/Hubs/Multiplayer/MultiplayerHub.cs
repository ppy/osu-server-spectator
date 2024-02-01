// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        protected readonly EntityStore<ServerMultiplayerRoom> Rooms;
        protected readonly MultiplayerHubContext HubContext;
        private readonly IDatabaseFactory databaseFactory;

        public MultiplayerHub(IDistributedCache cache, EntityStore<ServerMultiplayerRoom> rooms, EntityStore<MultiplayerClientState> users, IDatabaseFactory databaseFactory,
                              IHubContext<MultiplayerHub> hubContext)
            : base(cache, users)
        {
            Rooms = rooms;
            this.databaseFactory = databaseFactory;
            HubContext = new MultiplayerHubContext(hubContext, rooms, users);
        }

        public Task<MultiplayerRoom> JoinRoom(long roomId) => JoinRoomWithPassword(roomId, string.Empty);

        public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
        {
            Log($"Attempting to join room {roomId}");

            bool isRestricted;
            using (var db = databaseFactory.GetInstance())
                isRestricted = await db.IsUserRestrictedAsync(Context.GetUserId());

            if (isRestricted)
                throw new InvalidStateException("Can't join a room when restricted.");

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                if (userUsage.Item != null)
                {
                    // if the user already has a state, it means they are already in a room and can't join another without first leaving.
                    throw new InvalidStateException("Can't join a room when already in another room.");
                }

                // add the user to the room.
                var roomUser = new MultiplayerRoomUser(Context.GetUserId());

                // track whether this join necessitated starting the process of fetching the room and adding it to the room store.
                bool newRoomFetchStarted = false;

                ServerMultiplayerRoom? room = null;

                using (var roomUsage = await Rooms.GetForUse(roomId, true))
                {
                    try
                    {
                        if (roomUsage.Item == null)
                        {
                            newRoomFetchStarted = true;

                            // the requested room is not yet tracked by this server.
                            room = await retrieveRoom(roomId);

                            if (!string.IsNullOrEmpty(room.Settings.Password))
                            {
                                if (room.Settings.Password != password)
                                    throw new InvalidPasswordException();
                            }

                            // the above call will only succeed if this user is the host.
                            room.Host = roomUser;

                            // mark the room active - and wait for confirmation of this operation from the database - before adding the user to the room.
                            await markRoomActive(room);

                            roomUsage.Item = room;
                        }
                        else
                        {
                            room = roomUsage.Item;

                            // this is a sanity check to keep *rooms* in a good state.
                            // in theory the connection clean-up code should handle this correctly.
                            if (room.Users.Any(u => u.UserID == roomUser.UserID))
                                throw new InvalidOperationException($"User {roomUser.UserID} attempted to join room {room.RoomID} they are already present in.");
                        }

                        userUsage.Item = new MultiplayerClientState(Context.ConnectionId, Context.GetUserId(), roomId);

                        // because match type implementations may send subsequent information via Users collection hooks,
                        // inform clients before adding user to the room.
                        await Clients.Group(GetGroupId(roomId)).UserJoined(roomUser);

                        room.AddUser(roomUser);
                        room.UpdateForRetrieval();

                        await addDatabaseUser(room, roomUser);
                        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));

                        Log(room, "User joined");
                    }
                    catch
                    {
                        try
                        {
                            if (userUsage.Item != null)
                            {
                                // the user was joined to the room, so we can run the standard leaveRoom method.
                                // this will handle closing the room if this was the only user.
                                await leaveRoom(userUsage.Item, roomUsage, false);
                            }
                            else if (newRoomFetchStarted)
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
                            // no matter how we end up cleaning up the room, ensure the user's context is destroyed.
                            userUsage.Destroy();
                        }

                        throw;
                    }
                }

                var settings = new JsonSerializerSettings
                {
                    // explicitly use Auto here as we are not interested in the top level type being conveyed to the user.
                    TypeNameHandling = TypeNameHandling.Auto,
                };

                return JsonConvert.DeserializeObject<MultiplayerRoom>(JsonConvert.SerializeObject(room, settings), settings)
                       ?? throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Attempt to retrieve and construct a room from the database backend, based on a room ID specification.
        /// This will check the database backing to ensure things are in a consistent state.
        /// It should only be called by the room's host, before any other user has joined (and will throw if not).
        /// </summary>
        /// <param name="roomId">The proposed room ID.</param>
        /// <exception cref="InvalidOperationException">If anything is wrong with this request.</exception>
        private async Task<ServerMultiplayerRoom> retrieveRoom(long roomId)
        {
            Log($"Retrieving room {roomId} from database");

            using (var db = databaseFactory.GetInstance())
            {
                // TODO: this call should be transactional, and mark the room as managed by this server instance.
                // This will allow for other instances to know not to reinitialise the room if the host arrives there.
                // Alternatively, we can move lobby retrieval away from osu-web and not require this in the first place.
                // Needs further discussion and consideration either way.
                var databaseRoom = await db.GetRoomAsync(roomId);

                if (databaseRoom == null)
                    throw new InvalidOperationException("Specified match does not exist.");

                if (databaseRoom.ends_at != null && databaseRoom.ends_at < DateTimeOffset.Now)
                    throw new InvalidStateException("Match has already ended.");

                if (databaseRoom.user_id != Context.GetUserId())
                    throw new InvalidOperationException("Non-host is attempting to join match before host");

                var room = new ServerMultiplayerRoom(roomId, HubContext)
                {
                    Settings = new MultiplayerRoomSettings
                    {
                        Name = databaseRoom.name,
                        Password = databaseRoom.password,
                        MatchType = databaseRoom.type.ToMatchType(),
                        QueueMode = databaseRoom.queue_mode.ToQueueMode(),
                        AutoStartDuration = TimeSpan.FromSeconds(databaseRoom.auto_start_duration),
                        AutoSkip = databaseRoom.auto_skip
                    }
                };

                await room.Initialise(databaseFactory);

                return room;
            }
        }

        /// <summary>
        /// Marks a room active at the database, implying the host has joined and this server is now in control of the room's lifetime.
        /// </summary>
        private async Task markRoomActive(ServerMultiplayerRoom room)
        {
            Log(room, "Host marking room active");

            using (var db = databaseFactory.GetInstance())
                await db.MarkRoomActiveAsync(room);
        }

        public async Task LeaveRoom()
        {
            Log("Requesting to leave room");

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                if (userUsage.Item == null)
                    return;

                try
                {
                    await leaveRoom(userUsage.Item, false);
                }
                finally
                {
                    userUsage.Destroy();
                }
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
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var user = userUsage.Item;
                var room = roomUsage.Item;

                if (user == null)
                    throw new InvalidStateException("Local user was not found in the expected room");

                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                await Clients.User(userId.ToString()).Invited(user.UserId, room.RoomID, room.Settings.Password);
            }
        }

        public async Task TransferHost(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;

                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                Log(room, $"Transferring host from {room.Host?.UserID} to {userId}");

                ensureIsHost(room);

                var newHost = room.Users.FirstOrDefault(u => u.UserID == userId);

                if (newHost == null)
                    throw new Exception("Target user is not in the current room");

                await setNewHost(room, newHost);
            }
        }

        public async Task KickUser(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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
                    if (targetUserUsage.Item == null)
                        throw new InvalidOperationException();

                    try
                    {
                        await leaveRoom(targetUserUsage.Item, roomUsage, true);
                    }
                    finally
                    {
                        targetUserUsage.Destroy();
                    }
                }
            }
        }

        public async Task ChangeState(MultiplayerUserState newState)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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
                        if (isGameplayState(user.State))
                            return;

                        break;

                    // If a client a triggered gameplay state before they received the `Idle` message from their gameplay being aborted.
                    case MultiplayerUserState.Loaded:
                    case MultiplayerUserState.ReadyForGameplay:
                        if (!isGameplayState(user.State))
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

                await updateRoomStateIfRequired(room);
            }
        }

        public async Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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

        public async Task SendMatchRequest(MatchUserRequest request)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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
                        room.MatchTypeImplementation.HandleUserRequest(user, request);
                        break;
                }
            }
        }

        public async Task StartMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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

        public async Task AbortMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
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

                await updateRoomStateIfRequired(room);
            }
        }

        public async Task AbortGameplay()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                if (!isGameplayState(user.State))
                    throw new InvalidStateException("Cannot abort gameplay while not in a gameplay state");

                await HubContext.ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                await updateRoomStateIfRequired(room);
            }
        }

        public async Task AddPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                Log(room, $"Adding playlist item for beatmap {item.BeatmapID}");
                await room.Queue.AddItem(item, user);
                Log(room, $"Item ID {item.ID} added at slot {room.Queue.UpcomingItems.TakeWhile(i => i != item).Count() + 1} (of {room.Playlist.Count})");

                await updateRoomStateIfRequired(room);
            }
        }

        public async Task EditPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                Log(room, $"Editing playlist item {item.ID} for beatmap {item.BeatmapID}");
                await room.Queue.EditItem(item, user);
            }
        }

        public async Task RemovePlaylistItem(long playlistItemId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                Log(room, $"Removing playlist item {playlistItemId}");
                await room.Queue.RemoveItem(playlistItemId, user);

                await updateRoomStateIfRequired(room);
            }
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item))
            {
                var room = roomUsage.Item;

                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                if (room.State != MultiplayerRoomState.Open)
                    throw new InvalidStateException("Attempted to change settings while game is active");

                ensureIsHost(room);

                Log(room, "Settings updating");

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
                    room.ChangeMatchType(settings.MatchType);
                    Log(room, $"Switching room ruleset to {room.MatchTypeImplementation}");
                }

                if (previousSettings.QueueMode != settings.QueueMode)
                {
                    await room.Queue.UpdateFromQueueModeChange();
                    Log(room, $"Switching queue mode to {settings.QueueMode}");
                }

                await HubContext.NotifySettingsChanged(room, false);

                await updateRoomStateIfRequired(room);
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
            await leaveRoom(state, true);
            await base.CleanUpState(state);
        }

        private async Task setNewHost(MultiplayerRoom room, MultiplayerRoomUser newHost)
        {
            room.Host = newHost;
            await Clients.Group(GetGroupId(room.RoomID)).HostChanged(newHost.UserID);

            await updateDatabaseHost(room);
        }

        /// <summary>
        /// Should be called when user states change, to check whether the new overall room state can trigger a room-level state change.
        /// </summary>
        private async Task updateRoomStateIfRequired(ServerMultiplayerRoom room)
        {
            //check whether a room state change is required.
            switch (room.State)
            {
                case MultiplayerRoomState.Open:
                    if (room.Settings.AutoStartEnabled)
                    {
                        bool shouldHaveCountdown = !room.Queue.CurrentItem.Expired && room.Users.Any(u => u.State == MultiplayerUserState.Ready);

                        if (shouldHaveCountdown && !room.ActiveCountdowns.Any(c => c is MatchStartCountdown))
                            await room.StartCountdown(new MatchStartCountdown { TimeRemaining = room.Settings.AutoStartDuration }, HubContext.StartMatch);
                    }

                    break;

                case MultiplayerRoomState.WaitingForLoad:
                    int countGameplayUsers = room.Users.Count(u => isGameplayState(u.State));
                    int countReadyUsers = room.Users.Count(u => u.State == MultiplayerUserState.ReadyForGameplay);

                    // Attempt to start gameplay when no more users need to change states. If all users have aborted, this will abort the match.
                    if (countReadyUsers == countGameplayUsers)
                        await HubContext.StartOrStopGameplay(room);

                    break;

                case MultiplayerRoomState.Playing:
                    if (room.Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.FinishedPlay))
                            await HubContext.ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Results);

                        await HubContext.ChangeRoomState(room, MultiplayerRoomState.Open);
                        await Clients.Group(GetGroupId(room.RoomID)).ResultsReady();

                        await room.Queue.FinishCurrentItem();
                    }

                    break;
            }
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
                    if (isGameplayState(oldState))
                        throw new InvalidStateException("Cannot return to idle without aborting gameplay.");

                    // any non-gameplay state can return to idle.
                    break;

                case MultiplayerUserState.Ready:
                    if (oldState != MultiplayerUserState.Idle)
                        throw new InvalidStateChangeException(oldState, newState);

                    if (room.Queue.CurrentItem.Expired)
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

        private static bool isGameplayState(MultiplayerUserState state)
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
        private async Task<ItemUsage<ServerMultiplayerRoom>> getLocalUserRoom(MultiplayerClientState? state)
        {
            if (state == null)
                throw new NotJoinedRoomException();

            long roomId = state.CurrentRoomID;

            return await Rooms.GetForUse(roomId);
        }

        private async Task leaveRoom(MultiplayerClientState state, bool wasKick)
        {
            using (var roomUsage = await getLocalUserRoom(state))
                await leaveRoom(state, roomUsage, wasKick);
        }

        private async Task leaveRoom(MultiplayerClientState state, ItemUsage<ServerMultiplayerRoom> roomUsage, bool wasKick)
        {
            var room = roomUsage.Item;

            if (room == null)
                throw new InvalidOperationException("Attempted to operate on a null room");

            Log(room, wasKick ? "User kicked" : "User left");

            await Groups.RemoveFromGroupAsync(state.ConnectionId, GetGroupId(room.RoomID));

            var user = room.Users.FirstOrDefault(u => u.UserID == state.UserId);

            if (user == null)
                throw new InvalidStateException("User was not in the expected room.");

            room.RemoveUser(user);
            await removeDatabaseUser(room, user);

            // handle closing the room if the only participant is the user which is leaving.
            if (room.Users.Count == 0)
            {
                await endDatabaseMatch(room);

                // only destroy the usage after the database operation succeeds.
                Log(room, "Stopping tracking of room (all users left).");
                roomUsage.Destroy();
                return;
            }

            await updateRoomStateIfRequired(room);

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

        internal Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId) => Rooms.GetForUse(roomId);

        protected void Log(ServerMultiplayerRoom room, string message, LogLevel logLevel = LogLevel.Verbose) => base.Log($"[room:{room.RoomID}] {message}", logLevel);
    }
}
