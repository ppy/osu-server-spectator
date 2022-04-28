// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Allows communication with multiplayer clients from potentially outside of a direct <see cref="MultiplayerHub"/> context.
    /// </summary>
    public class MultiplayerHubContext : IMultiplayerHubContext
    {
        /// <summary>
        /// The amount of time allowed for players to finish loading gameplay before they're either forced into gameplay (if loaded) or booted to the menu (if still loading).
        /// </summary>
        private static readonly TimeSpan gameplay_load_timeout = TimeSpan.FromMinutes(1);

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

        public Task NotifyNewMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        public Task NotifyMatchRoomStateChanged(ServerMultiplayerRoom room)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), room.MatchState);
        }

        public Task NotifyMatchUserStateChanged(ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), user.UserID, user.MatchState);
        }

        public Task NotifyPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        public Task NotifyPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            await EnsureAllUsersValidMods(room);

            if (item.ID == room.Settings.PlaylistItemId)
                await UnreadyAllUsers(room);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        public async Task NotifySettingsChanged(ServerMultiplayerRoom room)
        {
            await EnsureAllUsersValidMods(room);

            // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
            await UnreadyAllUsers(room);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), room.Settings);
        }

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
            await room.StopCountdown();
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

        public async Task ChangeRoomState(ServerMultiplayerRoom room, MultiplayerRoomState newState)
        {
            log(room, null, $"Room state changing from {room.State} to {newState}");
            room.State = newState;
            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.RoomStateChanged), newState);
        }

        public async Task StartMatch(ServerMultiplayerRoom room)
        {
            if (room.State != MultiplayerRoomState.Open)
                throw new InvalidStateException("Can't start match when already in a running state.");

            if (room.Queue.CurrentItem.Expired)
                throw new InvalidStateException("Cannot start an expired playlist item.");

            var readyUsers = room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray();

            // If no users are ready, skip the current item in the queue.
            if (readyUsers.Length == 0)
            {
                await room.Queue.FinishCurrentItem();
                return;
            }

            foreach (var u in readyUsers)
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.WaitingForLoad);

            await ChangeRoomState(room, MultiplayerRoomState.WaitingForLoad);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID, true)).SendAsync(nameof(IMultiplayerClient.LoadRequested));

            await room.StartCountdown(new GameplayStartCountdown { TimeRemaining = gameplay_load_timeout }, StartOrStopGameplay);
        }

        public async Task StartOrStopGameplay(ServerMultiplayerRoom room)
        {
            Debug.Assert(room.State == MultiplayerRoomState.WaitingForLoad);

            await room.StopCountdown<GameplayStartCountdown>();

            bool anyUserPlaying = false;

            // Start gameplay for users that are able to, and abort the others which cannot.
            foreach (var user in room.Users)
            {
                string? connectionId = users.GetConnectionIdForUser(user.UserID);

                if (connectionId == null)
                    continue;

                if (user.CanStartGameplay())
                {
                    await ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Playing);
                    await context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.GameplayStarted));
                    anyUserPlaying = true;
                }
                else if (user.State == MultiplayerUserState.WaitingForLoad)
                {
                    await ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                    await context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.LoadAborted));
                    log(room, user, "Gameplay aborted because this user took too long to load.");
                }
            }

            await ChangeRoomState(room, anyUserPlaying ? MultiplayerRoomState.Playing : MultiplayerRoomState.Open);
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
