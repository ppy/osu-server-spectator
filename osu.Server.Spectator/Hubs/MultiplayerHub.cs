// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

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
        public bool TryGetRoom(long roomId, [MaybeNullWhen(false)] out MultiplayerRoom room)
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
                throw new UserAlreadyInMultiplayerRoom();
            }

            MultiplayerRoom? room;

            bool shouldBecomeHost = false;

            lock (active_rooms)
            {
                // check whether we are already aware of this match.

                if (!TryGetRoom(roomId, out room))
                {
                    // TODO: get details of the room from the database. hard abort if non existent.
                    active_rooms.Add(roomId, room = new MultiplayerRoom(roomId));
                    shouldBecomeHost = true;
                }
            }

            // add the user to the room.
            var roomUser = new MultiplayerRoomUser(CurrentContextUserId);

            room.PerformUpdate(r =>
            {
                r.Users.Add(roomUser);

                if (shouldBecomeHost)
                    r.Host = roomUser;
            });

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(roomId));
            await UpdateLocalUserState(new MultiplayerClientState(roomId));

            await Clients.Group(GetGroupId(roomId)).UserJoined(roomUser);

            return room;
        }

        public async Task LeaveRoom()
        {
            var room = await getLocalUserRoom();

            MultiplayerRoomUser? user = null;

            MultiplayerRoomUser? newHost = null;

            bool roomDisbanded = false;

            room.PerformUpdate(r =>
            {
                user = r.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    failWithInvalidState("User was not in the expected room.");

                room.Users.Remove(user);

                if (room.Users.Count == 0)
                {
                    roomDisbanded = true;
                    return;
                }

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    newHost = room.Users.FirstOrDefault();

                    if (newHost != null)
                        r.Host = newHost;
                }
            });

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID, true));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID));

            if (roomDisbanded)
            {
                lock (active_rooms)
                    active_rooms.Remove(room.RoomID);
            }
            else
            {
                var clients = Clients.Group(GetGroupId(room.RoomID));

                if (newHost != null)
                    await clients.HostChanged(newHost.UserID);
                await clients.UserLeft(user);
            }

            await RemoveLocalUserState();
        }

        public async Task TransferHost(long userId)
        {
            var room = await getLocalUserRoom();

            ensureIsHost(room);

            room.PerformUpdate(r =>
            {
                var newHost = r.Users.Find(u => u.UserID == userId);

                if (newHost == null)
                    throw new Exception("Target user is not in the current room");

                r.Host = newHost;
            });

            await Clients.Group(GetGroupId(room.RoomID)).HostChanged(userId);
        }

        public async Task ChangeState(MultiplayerUserState newState)
        {
            var room = await getLocalUserRoom();

            room.PerformUpdate(r =>
            {
                var user = r.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    failWithInvalidState("Local user was not found in the expected room");

                if (user.State == newState)
                    return;

                ensureValidStateSwitch(r, user.State, newState);
                user.State = newState;
            });

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

            //check whether a room state change is required.
            switch (room.State)
            {
                case MultiplayerRoomState.WaitingForLoad:
                    if (room.Users.All(u => u.State != MultiplayerUserState.WaitingForLoad))
                    {
                        foreach (var user in room.Users)
                            user.State = MultiplayerUserState.Playing;
                        await Clients.Group(GetGroupId(room.RoomID)).MatchStarted();

                        await changeRoomState(room, MultiplayerRoomState.Playing);
                    }

                    break;

                case MultiplayerRoomState.Playing:
                    if (room.Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        foreach (var user in room.Users)
                            user.State = MultiplayerUserState.Results;

                        await changeRoomState(room, MultiplayerRoomState.Open);
                        await Clients.Group(GetGroupId(room.RoomID)).ResultsReady();
                    }

                    break;
            }

            await Clients.Group(GetGroupId(room.RoomID)).UserStateChanged(CurrentContextUserId, newState);
        }

        public async Task StartMatch()
        {
            var room = await getLocalUserRoom();

            ensureIsHost(room);

            room.PerformUpdate(r =>
            {
                changeRoomState(room, MultiplayerRoomState.WaitingForLoad).Wait(); // todo: fix

                foreach (var user in r.Users)
                    user.State = MultiplayerUserState.WaitingForLoad;
            });

            await Clients.Group(GetGroupId(room.RoomID, true)).LoadRequested();
        }

        private async Task changeRoomState(MultiplayerRoom room, MultiplayerRoomState newState)
        {
            room.State = newState;
            await Clients.Group(GetGroupId(room.RoomID)).RoomStateChanged(MultiplayerRoomState.WaitingForLoad);
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            // todo: check the room isn't playing

            var state = await GetLocalUserState();
            if (state == null)
                throw new NotJoinedRoomException();

            long roomID = state.CurrentRoomID;

            if (!TryGetRoom(roomID, out var room))
                failWithInvalidState("User is in a room this hub is not aware of.");

            // todo: check this user has permission to change the settings of this room.

            room.PerformUpdate(r => r.Settings = settings);

            await Clients.Group(GetGroupId(roomID)).SettingsChanged(settings);
        }

        /// <summary>
        /// Get the group ID to be used for multiplayer messaging.
        /// </summary>
        /// <param name="roomId">The databased room ID.</param>
        /// <param name="gameplay">Whether the group ID should be for active gameplay, or room control messages.</param>
        public static string GetGroupId(long roomId, bool gameplay = false) => $"room:{roomId}:{gameplay}";

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
                        throw new InvalidStateChange(oldState, newState);

                    break;

                case MultiplayerUserState.WaitingForLoad:
                    // playing state is managed by the server.
                    throw new InvalidStateChange(oldState, newState);

                case MultiplayerUserState.Loaded:
                    if (oldState != MultiplayerUserState.WaitingForLoad)
                        throw new InvalidStateChange(oldState, newState);

                    break;

                case MultiplayerUserState.Playing:
                    // playing state is managed by the server.
                    throw new InvalidStateChange(oldState, newState);

                case MultiplayerUserState.FinishedPlay:
                    if (oldState != MultiplayerUserState.Playing)
                        throw new InvalidStateChange(oldState, newState);

                    break;

                case MultiplayerUserState.Results:
                    if (oldState != MultiplayerUserState.Playing)
                        throw new InvalidStateChange(oldState, newState);

                    break;

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
                    failWithInvalidState("User is in a room this hub is not aware of.");
            }

            return room;
        }

        [ExcludeFromCodeCoverage]
        [DoesNotReturn]
        private void failWithInvalidState(string message) => throw new InvalidStateException(message);
    }
}
