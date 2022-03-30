// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHubContext
    {
        private readonly IHubContext<MultiplayerHub> context;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;

        public MultiplayerHubContext(IHubContext<MultiplayerHub> context, EntityStore<ServerMultiplayerRoom> rooms, EntityStore<MultiplayerClientState> users)
        {
            this.context = context;
            this.rooms = rooms;
            this.users = users;
        }

        /// <summary>
        /// Send an immediate event to all clients in a room.
        /// </summary>
        /// <remarks>
        /// This should be used for events which have no permanent effect on state.
        /// For operations which are intended to persist (and be visible to new users which join a room) use <see cref="UpdateMatchRoomState"/> or <see cref="UpdateMatchUserState"/> instead.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="e">The event.</param>
        public Task SendMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        /// <summary>
        /// Let the hub know that the room's <see cref="MultiplayerRoom.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room whose state has changed.</param>
        public Task UpdateMatchRoomState(ServerMultiplayerRoom room)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), room.MatchState);
        }

        /// <summary>
        /// Let the hub know that the a user's <see cref="MultiplayerRoomUser.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="user">The user whose state has changed.</param>
        public Task UpdateMatchUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), user.UserID, user.MatchState);
        }

        /// <summary>
        /// Let the hub know that a playlist item has been added.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The added item.</param>
        public Task OnPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        /// <summary>
        /// Let the hub know that a playlist item has been removed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="playlistItemId">The removed item.</param>
        public Task OnPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        /// <summary>
        /// Let the hub know that a playlist item has been changed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The changed item.</param>
        public async Task OnPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            await EnsureAllUsersValidMods(room);

            if (item.ID == room.Settings.PlaylistItemId)
                await UnreadyAllUsers(room);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        /// <summary>
        /// Let the hub know that the room settings have been changed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        public async Task OnMatchSettingsChanged(ServerMultiplayerRoom room)
        {
            await EnsureAllUsersValidMods(room);

            // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
            await UnreadyAllUsers(room);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), room.Settings);
        }

        /// <summary>
        /// Retrieves a <see cref="ServerMultiplayerRoom"/> usage.
        /// </summary>
        /// <param name="roomId">The ID of the room to retrieve.</param>
        public Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId)
        {
            return rooms.GetForUse(roomId);
        }

        public async Task UnreadyAllUsers(ServerMultiplayerRoom room)
        {
            foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Idle);

            // Assume some destructive operation took place to warrant unreadying all users, and pre-emptively stop the countdown.
            // For example, gameplay-specific changes to the match settings or the current playlist item.
            room.StopCountdown();
        }

        public async Task EnsureAllUsersValidMods(ServerMultiplayerRoom room)
        {
            foreach (var user in room.Users)
            {
                if (!room.Queue.CurrentItem.ValidateUserMods(user.Mods, out var validMods))
                    await ChangeUserMods(validMods, room, user);
            }
        }

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods, ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            var newModList = newMods.ToList();

            if (!room.Queue.CurrentItem.ValidateUserMods(newModList, out var validMods))
                throw new InvalidStateException($"Incompatible mods were selected: {string.Join(',', newModList.Except(validMods).Select(m => m.Acronym))}");

            if (user.Mods.SequenceEqual(newModList))
                return;

            user.Mods = newModList;

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), user.UserID, newModList);
        }

        public async Task ChangeAndBroadcastUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state)
        {
            // Todo: How?
            // Log(room, $"User state changed from {user.State} to {state}");

            user.State = state;

            string? connectionId = users.GetConnectionIdForUser(user.UserID);

            if (connectionId != null)
            {
                switch (state)
                {
                    case MultiplayerUserState.FinishedPlay:
                    case MultiplayerUserState.Idle:
                        await context.Groups.RemoveFromGroupAsync(connectionId, MultiplayerHub.GetGroupId(room.RoomID, true));
                        break;

                    case MultiplayerUserState.Ready:
                    case MultiplayerUserState.Spectating:
                        await context.Groups.AddToGroupAsync(connectionId, MultiplayerHub.GetGroupId(room.RoomID, true));
                        break;
                }
            }

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserStateChanged), user.UserID, user.State);
        }
    }
}
