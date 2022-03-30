// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// Allows communication with multiplayer clients outside of a direct <see cref="MultiplayerHub"/> context.
    /// </summary>
    public class MultiplayerHubContext
    {
        private readonly IHubContext<MultiplayerHub> context;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;
        private readonly Logger logger;

        public MultiplayerHubContext(IHubContext<MultiplayerHub> context, EntityStore<ServerMultiplayerRoom> rooms, EntityStore<MultiplayerClientState> users)
        {
            this.context = context;
            this.rooms = rooms;
            this.users = users;

            logger = Logger.GetLogger(nameof(MultiplayerHub).Replace("Hub", string.Empty));
        }

        /// <summary>
        /// Notifies users in a room of an event.
        /// </summary>
        /// <remarks>
        /// This should be used for events which have no permanent effect on state.
        /// For operations which are intended to persist (and be visible to new users which join a room) use <see cref="NotifyMatchRoomStateChanged"/> or <see cref="NotifyMatchUserStateChanged"/> instead.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="e">The event.</param>
        public Task NotifyNewMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        /// <summary>
        /// Notify users in a room that the room's <see cref="MultiplayerRoom.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room whose state has changed.</param>
        public Task NotifyMatchRoomStateChanged(ServerMultiplayerRoom room)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), room.MatchState);
        }

        /// <summary>
        /// Notifies users in a room that a user's <see cref="MultiplayerRoomUser.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="user">The user whose state has changed.</param>
        public Task NotifyMatchUserStateChanged(ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), user.UserID, user.MatchState);
        }

        /// <summary>
        /// Notifies users in a room that a playlist item has been added.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The added item.</param>
        public Task NotifyPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        /// <summary>
        /// Notifies users in a room that a playlist item has been removed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="playlistItemId">The removed item.</param>
        public Task NotifyPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        /// <summary>
        /// Notifies users in a room that a playlist item has been changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, and unreadies all users and stops the current countdown if the currently-selected playlist item was changed.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The changed item.</param>
        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            await EnsureAllUsersValidMods(room);

            if (item.ID == room.Settings.PlaylistItemId)
                await UnreadyAllUsers(room);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        /// <summary>
        /// Notifies users in a room that the room's settings have changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, unreadies all users, and stops the current countdown.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        public async Task NotifySettingsChanged(ServerMultiplayerRoom room)
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

        /// <summary>
        /// Unreadies all users in a room.
        /// </summary>
        /// <remarks>
        /// Stops the current countdown.
        /// </remarks>
        /// <param name="room">The room to unready users in.</param>
        public async Task UnreadyAllUsers(ServerMultiplayerRoom room)
        {
            foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Idle);

            // Assume some destructive operation took place to warrant unreadying all users, and pre-emptively stop the countdown.
            // For example, gameplay-specific changes to the match settings or the current playlist item.
            room.StopCountdown();
        }

        /// <summary>
        /// Adjusts user mod selections to ensure they're valid for the current playlist item.
        /// </summary>
        /// <param name="room">The room to validate user mods in.</param>
        public async Task EnsureAllUsersValidMods(ServerMultiplayerRoom room)
        {
            foreach (var user in room.Users)
            {
                if (!room.Queue.CurrentItem.ValidateUserMods(user.Mods, out var validMods))
                    await ChangeUserMods(validMods, room, user);
            }
        }

        /// <summary>
        /// Changes a user's mods in a room.
        /// </summary>
        /// <param name="newMods">The new mod selection.</param>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <exception cref="InvalidStateException">If the new selection is not valid for current playlist item.</exception>
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

        /// <summary>
        /// Changes a user's state in a room.
        /// </summary>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <param name="state">The new state.</param>
        public async Task ChangeAndBroadcastUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state)
        {
            log(room, user, $"User state changed from {user.State} to {state}");

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

        private void log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Verbose)
        {
            logger.Add($"[user:{getLoggableUserIdentifier(user)}] [room:{room.RoomID}] {message.Trim()}", logLevel);
        }

        private void error(MultiplayerRoomUser? user, string message, Exception exception)
        {
            logger.Add($"[user:{getLoggableUserIdentifier(user)}] {message.Trim()}", LogLevel.Error, exception);
        }

        private string getLoggableUserIdentifier(MultiplayerRoomUser? user)
        {
            return user?.UserID.ToString() ?? "???";
        }
    }
}
