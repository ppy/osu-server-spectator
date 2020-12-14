// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.RealtimeMultiplayer;
using osu.Server.Spectator.DatabaseModels;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        // for the time being rooms will be maintained in memory and not distributed.
        private static readonly Dictionary<long, MultiplayerRoom> active_rooms = new Dictionary<long, MultiplayerRoom>();

        /// <summary>
        /// Temporary method to reset in-memory storage (used only for tests).
        /// </summary>
        public static void Reset()
        {
            Console.WriteLine("Resetting ALL tracked rooms.");
            lock (active_rooms)
                active_rooms.Clear();
        }

        public MultiplayerHub(IDistributedCache cache)
            : base(cache)
        {
        }

        /// <summary>
        /// Retrieve a room instance from a provided room ID, if tracked by this hub.
        /// </summary>
        /// <param name="roomId">The lookup ID.</param>
        /// <param name="room">The room instance, or null if not tracked.</param>
        /// <returns>Whether the room could be found.</returns>
        protected bool TryGetRoom(long roomId, [MaybeNullWhen(false)] out MultiplayerRoom room)
        {
            lock (active_rooms)
                return active_rooms.TryGetValue(roomId, out room);
        }

        public async Task<MultiplayerRoom> JoinRoom(long roomId)
        {
            var state = await GetLocalUserState();

            if (state != null)
            {
                // if the user already has a state, it means they are already in a room and can't join another without first leaving.
                throw new InvalidStateException("Can't join a room when already in another room.");
            }

            // add the user to the room.
            var roomUser = new MultiplayerRoomUser(CurrentContextUserId);

            // check whether we are already aware of this match.
            if (!TryGetRoom(roomId, out var room))
            {
                room = await RetrieveRoom(roomId);

                room.Host = roomUser;

                lock (active_rooms)
                {
                    Console.WriteLine($"Tracking new room {roomId}.");
                    active_rooms.Add(roomId, room);
                }
            }

            using (room.LockForUpdate())
            {
                room.Users.Add(roomUser);
                await Clients.Group(GetGroupId(roomId)).UserJoined(roomUser);
                await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));

                await updateDatabaseParticipants(room);
            }

            await UpdateLocalUserState(new MultiplayerClientState(roomId));

            return room;
        }

        /// <summary>
        /// Attempt to construct and a room based on a room ID specification.
        /// This will check the database backing to ensure things are in a consistent state.
        /// </summary>
        /// <param name="roomId">The proposed room ID.</param>
        /// <exception cref="InvalidStateException">If anything is wrong with this request.</exception>
        protected virtual async Task<MultiplayerRoom> RetrieveRoom(long roomId)
        {
            using (var conn = Database.GetConnection())
            {
                var databaseRoom = await conn.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM multiplayer_rooms WHERE category = 'realtime' AND id = @roomId", new { roomId });
                if (databaseRoom == null)
                    throw new InvalidStateException("Specified match does not exist.");

                if (databaseRoom.ends_at != null)
                    throw new InvalidStateException("Match has already ended.");

                if (databaseRoom.user_id != CurrentContextUserId)
                    throw new InvalidStateException("Non-host is attempting to join match before host");

                var playlistItem = await conn.QuerySingleAsync<multiplayer_playlist_item>("SELECT * FROM multiplayer_playlist_items WHERE room_id = @roomId", new { roomId });

                return new MultiplayerRoom(roomId)
                {
                    Settings = new MultiplayerRoomSettings
                    {
                        BeatmapID = playlistItem.beatmap_id,
                        RulesetID = playlistItem.ruleset_id,
                        Name = databaseRoom.name,
                        Mods = playlistItem.allowed_mods != null ? JsonConvert.DeserializeObject<IEnumerable<APIMod>>(playlistItem.allowed_mods) : Array.Empty<APIMod>()
                    }
                };
            }
        }

        public async Task LeaveRoom()
        {
            var room = await getLocalUserRoom();

            await RemoveLocalUserState();

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID, true));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID));

            using (room.LockForUpdate())
            {
                var user = room.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    failWithInvalidState("User was not in the expected room.");

                room.Users.Remove(user);

                await updateDatabaseParticipants(room);

                if (room.Users.Count == 0)
                {
                    lock (active_rooms)
                    {
                        Console.WriteLine($"Stopping tracking of room {room.RoomID} (all users left).");
                        active_rooms.Remove(room.RoomID);
                    }

                    using (var conn = Database.GetConnection())
                        await conn.ExecuteAsync("UPDATE multiplayer_rooms SET ends_at = NOW() WHERE id = @RoomID", new { room.RoomID });

                    return;
                }

                var clients = Clients.Group(GetGroupId(room.RoomID));

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    var newHost = room.Users.FirstOrDefault();

                    if (newHost != null)
                    {
                        room.Host = newHost;
                        await clients.HostChanged(newHost.UserID);
                    }
                }

                await clients.UserLeft(user);
            }
        }

        public async Task TransferHost(long userId)
        {
            var room = await getLocalUserRoom();

            using (room.LockForUpdate())
            {
                ensureIsHost(room);

                var newHost = room.Users.Find(u => u.UserID == userId);

                if (newHost == null)
                    throw new Exception("Target user is not in the current room");

                room.Host = newHost;
                await Clients.Group(GetGroupId(room.RoomID)).HostChanged(userId);
            }
        }

        public async Task ChangeState(MultiplayerUserState newState)
        {
            var room = await getLocalUserRoom();

            using (room.LockForUpdate())
            {
                var user = room.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    failWithInvalidState("Local user was not found in the expected room");

                if (user.State == newState)
                    return;

                ensureValidStateSwitch(room, user.State, newState);
                user.State = newState;

                // handle whether this user should be receiving gameplay messages or not.
                switch (newState)
                {
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

        public async Task StartMatch()
        {
            var room = await getLocalUserRoom();

            ensureIsHost(room);

            using (room.LockForUpdate())
            {
                if (room.State != MultiplayerRoomState.Open)
                    throw new InvalidStateException("Can't start match when already in a running state.");

                var readyUsers = room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray();

                if (readyUsers.Length == 0)
                    throw new InvalidStateException("Can't start match when no users are ready.");

                if (room.Host != null && room.Host.State != MultiplayerUserState.Ready)
                    throw new InvalidStateException("Can't start match when the host is not ready.");

                await changeRoomState(room, MultiplayerRoomState.WaitingForLoad);

                foreach (var u in readyUsers)
                    await changeAndBroadcastUserState(room, u, MultiplayerUserState.WaitingForLoad);

                await Clients.Group(GetGroupId(room.RoomID, true)).LoadRequested();
            }
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            var room = await getLocalUserRoom();

            using (room.LockForUpdate())
            {
                if (room.State != MultiplayerRoomState.Open)
                    throw new InvalidStateException("Attempted to change settings while game is active");

                ensureIsHost(room);

                room.Settings = settings;

                using (var conn = Database.GetConnection())
                {
                    var dbPlaylistItem = new multiplayer_playlist_item(room);

                    await conn.ExecuteAsync("UPDATE multiplayer_rooms SET name = @Name WHERE id = @RoomID", new { room.Settings.Name, room.RoomID });
                    await conn.ExecuteAsync("UPDATE multiplayer_playlist_items SET beatmap_id = @beatmap_id, ruleset_id = @ruleset_id, required_mods = @required_mods, updated_at = NOW() WHERE room_id = @room_id", dbPlaylistItem);
                }

                await Clients.Group(GetGroupId(room.RoomID)).SettingsChanged(settings);
            }
        }

        protected override Task OnDisconnectedAsync(Exception exception, MultiplayerClientState? state)
        {
            if (state != null)
                return LeaveRoom();

            return base.OnDisconnectedAsync(exception, state);
        }

        /// <summary>
        /// Get the group ID to be used for multiplayer messaging.
        /// </summary>
        /// <param name="roomId">The databased room ID.</param>
        /// <param name="gameplay">Whether the group ID should be for active gameplay, or room control messages.</param>
        public static string GetGroupId(long roomId, bool gameplay = false) => $"room:{roomId}:{gameplay}";

        private async Task updateDatabaseParticipants(MultiplayerRoom room)
        {
            using (var conn = Database.GetConnection())
            {
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    // This should be considered *very* temporary, and for display purposes only!
                    await conn.ExecuteAsync("DELETE FROM multiplayer_rooms_high WHERE room_id = @RoomID", new { room.RoomID, room.Users.Count }, transaction);

                    foreach (var u in room.Users)
                        await conn.ExecuteAsync("INSERT INTO multiplayer_rooms_high (room_id, user_id) VALUES (@RoomID, @UserID)", new { room.RoomID, u.UserID }, transaction);

                    await transaction.CommitAsync();
                }

                await conn.ExecuteAsync("UPDATE multiplayer_rooms SET participant_count = @Count WHERE id = @RoomID", new { room.RoomID, room.Users.Count });
            }
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
        private async Task<MultiplayerRoom> getLocalUserRoom()
        {
            var state = await GetLocalUserState();

            if (state == null)
                throw new NotJoinedRoomException();

            long roomId = state.CurrentRoomID;

            MultiplayerRoom? room;

            lock (active_rooms)
            {
                if (!active_rooms.TryGetValue(roomId, out room))
                    failWithInvalidState($"User ({CurrentContextUserId}) is in a room ({roomId}) this hub is not aware of.");
            }

            return room;
        }

        [ExcludeFromCodeCoverage]
        [DoesNotReturn]
        private void failWithInvalidState(string message) => throw new InvalidStateException(message);
    }
}
