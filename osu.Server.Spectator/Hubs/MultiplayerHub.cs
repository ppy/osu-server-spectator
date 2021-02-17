// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        protected static readonly EntityStore<MultiplayerRoom> ACTIVE_ROOMS = new EntityStore<MultiplayerRoom>();

        private readonly IDatabaseFactory databaseFactory;

        public MultiplayerHub(IDistributedCache cache, IDatabaseFactory databaseFactory)
            : base(cache)
        {
            this.databaseFactory = databaseFactory;
        }

        /// <summary>
        /// Temporary method to reset in-memory storage (used only for tests).
        /// </summary>
        public new static void Reset()
        {
            StatefulUserHub<IMultiplayerClient, MultiplayerClientState>.Reset();

            Console.WriteLine("Resetting ALL tracked rooms.");
            ACTIVE_ROOMS.Clear();
        }

        public async Task<MultiplayerRoom> JoinRoom(long roomId)
        {
            Log($"Joining room {roomId}");

            bool isRestricted;
            using (var db = databaseFactory.GetInstance())
                isRestricted = await db.IsUserRestrictedAsync(CurrentContextUserId);

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
                var roomUser = new MultiplayerRoomUser(CurrentContextUserId);

                // track whether this join necessitated starting the process of fetching the room and adding it to the ACTIVE_ROOMS store.
                bool newRoomFetchStarted = false;

                MultiplayerRoom? room = null;

                using (var roomUsage = await ACTIVE_ROOMS.GetForUse(roomId, true))
                {
                    try
                    {
                        if (roomUsage.Item == null)
                        {
                            newRoomFetchStarted = true;

                            // the requested room is not yet tracked by this server.
                            room = await retrieveRoom(roomId);

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

                        userUsage.Item = new MultiplayerClientState(Context.ConnectionId, CurrentContextUserId, roomId);
                        room.Users.Add(roomUser);

                        await updateDatabaseParticipants(room);

                        await Clients.Group(GetGroupId(roomId)).UserJoined(roomUser);
                        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));

                        Log($"Joined room {room.RoomID}");
                    }
                    catch
                    {
                        try
                        {
                            if (userUsage.Item != null)
                            {
                                // the user was joined to the room, so we can run the standard leaveRoom method.
                                // this will handle closing the room if this was the only user.
                                await leaveRoom(userUsage.Item, roomUsage);
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

                return JsonConvert.DeserializeObject<MultiplayerRoom>(JsonConvert.SerializeObject(room));
            }
        }

        /// <summary>
        /// Attempt to retrieve and construct a room from the database backend, based on a room ID specification.
        /// This will check the database backing to ensure things are in a consistent state.
        /// It should only be called by the room's host, before any other user has joined (and will throw if not).
        /// </summary>
        /// <param name="roomId">The proposed room ID.</param>
        /// <exception cref="InvalidStateException">If anything is wrong with this request.</exception>
        private async Task<MultiplayerRoom> retrieveRoom(long roomId)
        {
            Log($"Retrieving room {roomId} from database");

            using (var db = databaseFactory.GetInstance())
            {
                var databaseRoom = await db.GetRoomAsync(roomId);

                if (databaseRoom == null)
                    throw new InvalidStateException("Specified match does not exist.");

                if (databaseRoom.ends_at != null && databaseRoom.ends_at < DateTimeOffset.Now)
                    throw new InvalidStateException("Match has already ended.");

                if (databaseRoom.user_id != CurrentContextUserId)
                    throw new InvalidStateException("Non-host is attempting to join match before host");

                var playlistItem = await db.GetCurrentPlaylistItemAsync(roomId);
                var beatmapChecksum = await db.GetBeatmapChecksumAsync(playlistItem.beatmap_id);

                return new MultiplayerRoom(roomId)
                {
                    Settings = new MultiplayerRoomSettings
                    {
                        BeatmapChecksum = beatmapChecksum,
                        BeatmapID = playlistItem.beatmap_id,
                        RulesetID = playlistItem.ruleset_id,
                        Name = databaseRoom.name,
                        RequiredMods = playlistItem.required_mods != null ? JsonConvert.DeserializeObject<IEnumerable<APIMod>>(playlistItem.required_mods) : Array.Empty<APIMod>(),
                        AllowedMods = playlistItem.allowed_mods != null ? JsonConvert.DeserializeObject<IEnumerable<APIMod>>(playlistItem.allowed_mods) : Array.Empty<APIMod>(),
                        PlaylistItemId = playlistItem.id
                    }
                };
            }
        }

        /// <summary>
        /// Marks a room active at the database, implying the host has joined and this server is now in control of the room's lifetime.
        /// </summary>
        private async Task markRoomActive(MultiplayerRoom room)
        {
            Log($"Host marking room active {room.RoomID}");

            using (var db = databaseFactory.GetInstance())
                await db.MarkRoomActiveAsync(room);
        }

        public async Task LeaveRoom()
        {
            Log("Requesting to leave room");

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                if (userUsage.Item == null)
                    throw new NotJoinedRoomException();

                try
                {
                    await leaveRoom(userUsage.Item);
                }
                finally
                {
                    userUsage.Destroy();
                }
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

                ensureIsHost(room);

                var newHost = room.Users.Find(u => u.UserID == userId);

                if (newHost == null)
                    throw new Exception("Target user is not in the current room");

                await setNewHost(room, newHost);
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

                var user = room.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    throw new InvalidStateException("Local user was not found in the expected room");

                if (user.State == newState)
                    return;

                ensureValidStateSwitch(room, user.State, newState);
                user.State = newState;

                // handle whether this user should be receiving gameplay messages or not.
                switch (newState)
                {
                    case MultiplayerUserState.FinishedPlay:
                    case MultiplayerUserState.Idle:
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID, true));
                        break;

                    case MultiplayerUserState.Ready:
                        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID, true));
                        break;
                }

                await Clients.Group(GetGroupId(room.RoomID)).UserStateChanged(CurrentContextUserId, newState);

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

                var user = room.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                if (user.BeatmapAvailability.Equals(newBeatmapAvailability))
                    return;

                user.BeatmapAvailability = newBeatmapAvailability;

                await Clients.Group(GetGroupId(room.RoomID)).UserBeatmapAvailabilityChanged(CurrentContextUserId, newBeatmapAvailability);
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

                var user = room.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                var oldModList = user.Mods.ToList();
                var newModList = newMods.ToList();

                if (oldModList.SequenceEqual(newModList))
                    return;

                user.Mods = newModList;

                await Clients.Group(GetGroupId(room.RoomID)).UserModsChanged(CurrentContextUserId, newModList);
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

                if (room.State != MultiplayerRoomState.Open)
                    throw new InvalidStateException("Can't start match when already in a running state.");

                var readyUsers = room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray();

                if (readyUsers.Length == 0)
                    throw new InvalidStateException("Can't start match when no users are ready.");

                if (room.Host != null && room.Host.State != MultiplayerUserState.Ready)
                    throw new InvalidStateException("Can't start match when the host is not ready.");

                foreach (var u in readyUsers)
                    await changeAndBroadcastUserState(room, u, MultiplayerUserState.WaitingForLoad);

                await changeRoomState(room, MultiplayerRoomState.WaitingForLoad);

                await Clients.Group(GetGroupId(room.RoomID, true)).LoadRequested();

                await commitPlaylistItem(room);
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

                if (room.Settings.Equals(settings))
                    return;

                var previousSettings = room.Settings;

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

                // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
                foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                    await changeAndBroadcastUserState(room, u, MultiplayerUserState.Idle);

                await Clients.Group(GetGroupId(room.RoomID)).SettingsChanged(settings);
            }
        }

        /// <summary>
        /// Get the group ID to be used for multiplayer messaging.
        /// </summary>
        /// <param name="roomId">The databased room ID.</param>
        /// <param name="gameplay">Whether the group ID should be for active gameplay, or room control messages.</param>
        public static string GetGroupId(long roomId, bool gameplay = false) => $"room:{roomId}:{gameplay}";

        private async Task commitPlaylistItem(MultiplayerRoom room)
        {
            multiplayer_playlist_item currentItem;

            using (var db = databaseFactory.GetInstance())
            {
                // Don't trust the playlist item ID from clients - re-retrieve using the server's own knowledge.
                currentItem = await db.GetCurrentPlaylistItemAsync(room.RoomID);

                await db.ExpirePlaylistItemAsync(currentItem.id);
            }

            // Todo: Only run the following code for non-host-rotate matches.
            long newPlaylistItemId;
            using (var db = databaseFactory.GetInstance())
                newPlaylistItemId = await db.CreatePlaylistItemAsync(currentItem);

            // Distribute the new playlist item ID to clients. All future playlist changes will affect this new one.
            room.Settings.PlaylistItemId = newPlaylistItemId;
            await Clients.Group(GetGroupId(room.RoomID)).SettingsChanged(room.Settings);
        }

        private async Task updateDatabaseSettings(MultiplayerRoom room)
        {
            using (var db = databaseFactory.GetInstance())
            {
                var dbPlaylistItem = new multiplayer_playlist_item(room);

                string beatmapChecksum = await db.GetBeatmapChecksumAsync(dbPlaylistItem.beatmap_id);

                if (beatmapChecksum == null)
                    throw new InvalidStateException("Attempted to select a beatmap which does not exist online.");

                if (room.Settings.BeatmapChecksum != beatmapChecksum)
                    throw new InvalidStateException("Attempted to select a beatmap which has been modified.");

                await db.UpdateRoomSettingsAsync(room);
            }
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

        private async Task updateDatabaseParticipants(MultiplayerRoom room)
        {
            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomParticipantsAsync(room);
        }

        protected override async Task CleanUpState(MultiplayerClientState state)
        {
            await leaveRoom(state);
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
        private async Task updateRoomStateIfRequired(MultiplayerRoom room)
        {
            //check whether a room state change is required.
            switch (room.State)
            {
                case MultiplayerRoomState.WaitingForLoad:
                    if (room.Users.All(u => u.State != MultiplayerUserState.WaitingForLoad))
                    {
                        var loadedUsers = room.Users.Where(u => u.State == MultiplayerUserState.Loaded).ToArray();

                        if (loadedUsers.Length == 0)
                        {
                            // all users have bailed from the load sequence. cancel the game start.
                            await changeRoomState(room, MultiplayerRoomState.Open);
                            return;
                        }

                        foreach (var u in loadedUsers)
                            await changeAndBroadcastUserState(room, u, MultiplayerUserState.Playing);

                        await Clients.Group(GetGroupId(room.RoomID)).MatchStarted();

                        await changeRoomState(room, MultiplayerRoomState.Playing);
                    }

                    break;

                case MultiplayerRoomState.Playing:
                    if (room.Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.FinishedPlay))
                            await changeAndBroadcastUserState(room, u, MultiplayerUserState.Results);

                        await changeRoomState(room, MultiplayerRoomState.Open);
                        await Clients.Group(GetGroupId(room.RoomID)).ResultsReady();
                    }

                    break;
            }
        }

        private Task changeAndBroadcastUserState(MultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state)
        {
            user.State = state;
            return Clients.Group(GetGroupId(room.RoomID)).UserStateChanged(user.UserID, user.State);
        }

        /// <summary>
        /// Changes the provided room's state and notifies all users.
        /// </summary>
        private async Task changeRoomState(MultiplayerRoom room, MultiplayerRoomState newState)
        {
            room.State = newState;
            await Clients.Group(GetGroupId(room.RoomID)).RoomStateChanged(newState);
        }

        /// <summary>
        /// Given a room and a state transition, throw if there's an issue with the sequence of events.
        /// </summary>
        /// <param name="room">The room.</param>
        /// <param name="oldState">The old state.</param>
        /// <param name="newState">The new state.</param>
        private void ensureValidStateSwitch(MultiplayerRoom room, MultiplayerUserState oldState, MultiplayerUserState newState)
        {
            switch (newState)
            {
                case MultiplayerUserState.Idle:
                    // any state can return to idle.
                    break;

                case MultiplayerUserState.Ready:
                    if (oldState != MultiplayerUserState.Idle)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.WaitingForLoad:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.Loaded:
                    if (oldState != MultiplayerUserState.WaitingForLoad)
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

                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }

        /// <summary>
        /// Ensure the local user is the host of the room, and throw if they are not.
        /// </summary>
        private void ensureIsHost(MultiplayerRoom room)
        {
            if (room.Host?.UserID != CurrentContextUserId)
                throw new NotHostException();
        }

        /// <summary>
        /// Retrieve the <see cref="MultiplayerRoom"/> for the local context user.
        /// </summary>
        private async Task<ItemUsage<MultiplayerRoom>> getLocalUserRoom(MultiplayerClientState? state)
        {
            if (state == null)
                throw new NotJoinedRoomException();

            long roomId = state.CurrentRoomID;

            return await ACTIVE_ROOMS.GetForUse(roomId);
        }

        private async Task leaveRoom(MultiplayerClientState state)
        {
            using (var roomUsage = await getLocalUserRoom(state))
                await leaveRoom(state, roomUsage);
        }

        private async Task leaveRoom(MultiplayerClientState state, ItemUsage<MultiplayerRoom> roomUsage)
        {
            var room = roomUsage.Item;

            if (room == null)
                throw new InvalidOperationException("Attempted to operate on a null room");

            Log($"Leaving room {room.RoomID}");

            await Groups.RemoveFromGroupAsync(state.ConnectionId, GetGroupId(room.RoomID, true));
            await Groups.RemoveFromGroupAsync(state.ConnectionId, GetGroupId(room.RoomID));

            var user = room.Users.Find(u => u.UserID == state.UserId);

            if (user == null)
                throw new InvalidStateException("User was not in the expected room.");

            // handle closing the room if the only participant is the user which is leaving.
            if (room.Users.Count == 1)
            {
                await endDatabaseMatch(room);

                // only destroy the usage after the database operation succeeds.
                Log($"Stopping tracking of room {room.RoomID} (all users left).");
                roomUsage.Destroy();
                return;
            }

            room.Users.Remove(user);
            await updateDatabaseParticipants(room);

            var clients = Clients.Group(GetGroupId(room.RoomID));

            // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
            if (room.Host?.Equals(user) == true)
            {
                // there *has* to still be at least one user in the room (see user check above).
                var newHost = room.Users.First();

                await setNewHost(room, newHost);
            }

            await clients.UserLeft(user);
        }
    }
}
