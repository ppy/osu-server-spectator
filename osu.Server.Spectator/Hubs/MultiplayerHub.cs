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

        public MultiplayerHub([JetBrains.Annotations.NotNull] IDistributedCache cache)
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

            room.PerformUpdate(r =>
            {
                user = r.Users.Find(u => u.UserID == CurrentContextUserId);

                if (user == null)
                    failWithInvalidState("User was not in the expected room.");

                room.Users.Remove(user);

                // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
                if (room.Host?.Equals(user) == true)
                {
                    newHost = room.Users.FirstOrDefault();

                    if (newHost != null)
                        r.Host = newHost;
                }
            });

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(room.RoomID));

            if (newHost != null)
                await Clients.Group(GetGroupId(room.RoomID)).HostChanged(newHost.UserID);
            await Clients.Group(GetGroupId(room.RoomID)).UserLeft(user);

            await RemoveLocalUserState();
        }

        public async Task TransferHost(long userId)
        {
            var room = await getLocalUserRoom();

            room.PerformUpdate(r =>
            {
                var newHost = r.Users.Find(u => u.UserID == userId);

                if (newHost == null)
                    throw new Exception("Target user is not in the current room");

                r.Host = newHost;
            });

            await Clients.Group(GetGroupId(room.RoomID)).HostChanged(userId);
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
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

        public static string GetGroupId(long roomId) => $"room:{roomId}";

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
