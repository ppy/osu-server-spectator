// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Allows communication with multiplayer clients from potentially outside of a direct <see cref="MultiplayerHub"/> context.
    /// </summary>
    public interface IMultiplayerHubContext
    {
        /// <summary>
        /// Notifies users in a room of an event.
        /// </summary>
        /// <remarks>
        /// This should be used for events which have no permanent effect on state.
        /// For operations which are intended to persist (and be visible to new users which join a room) use <see cref="NotifyMatchRoomStateChanged"/> or <see cref="NotifyMatchUserStateChanged"/> instead.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="e">The event.</param>
        Task NotifyNewMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e);

        /// <summary>
        /// Notify users in a room that the room's <see cref="MultiplayerRoom.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room whose state has changed.</param>
        Task NotifyMatchRoomStateChanged(ServerMultiplayerRoom room);

        /// <summary>
        /// Notifies users in a room that a user's <see cref="MultiplayerRoomUser.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="user">The user whose state has changed.</param>
        Task NotifyMatchUserStateChanged(ServerMultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Notifies users in a room that a playlist item has been added.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The added item.</param>
        Task NotifyPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item);

        /// <summary>
        /// Notifies users in a room that a playlist item has been removed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="playlistItemId">The removed item.</param>
        Task NotifyPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId);

        /// <summary>
        /// Notifies users in a room that a playlist item has been changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, and unreadies all users and stops the current countdown if the currently-selected playlist item was changed.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The changed item.</param>
        /// <param name="beatmapChanged">Whether the beatmap changed.</param>
        Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item, bool beatmapChanged);

        /// <summary>
        /// Notifies users in a room that the room's settings have changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, unreadies all users, and stops the current countdown.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="playlistItemChanged">Whether the current playlist item changed.</param>
        Task NotifySettingsChanged(ServerMultiplayerRoom room, bool playlistItemChanged);

        /// <summary>
        /// Retrieves a <see cref="ServerMultiplayerRoom"/> usage.
        /// </summary>
        /// <param name="roomId">The ID of the room to retrieve.</param>
        Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId);

        /// <summary>
        /// Unreadies all users in a room.
        /// </summary>
        /// <remarks>
        /// Stops the current countdown.
        /// </remarks>
        /// <param name="room">The room to unready users in.</param>
        /// <param name="resetBeatmapAvailability">Whether to reset availabilities (ie. if the beatmap changed).</param>
        Task UnreadyAllUsers(ServerMultiplayerRoom room, bool resetBeatmapAvailability);

        /// <summary>
        /// Changes a user's style in a room.
        /// </summary>
        /// <param name="beatmapId">The new beatmap selection.</param>
        /// <param name="rulesetId">The new ruleset selection.</param>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <exception cref="InvalidStateException">If the new selection is not valid for current playlist item.</exception>
        Task ChangeUserStyle(int? beatmapId, int? rulesetId, ServerMultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Changes a user's mods in a room.
        /// </summary>
        /// <param name="newMods">The new mod selection.</param>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <exception cref="InvalidStateException">If the new selection is not valid for current playlist item.</exception>
        Task ChangeUserMods(IEnumerable<APIMod> newMods, ServerMultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Changes a user's state in a room.
        /// </summary>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <param name="state">The new state.</param>
        Task ChangeAndBroadcastUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state);

        /// <summary>
        /// Changes a user's beatmap availability for the current playlist item.
        /// </summary>
        /// <param name="room">The room containing the user.</param>
        /// <param name="user">The user.</param>
        /// <param name="availability">The new availability.</param>
        Task ChangeAndBroadcastUserBeatmapAvailability(ServerMultiplayerRoom room, MultiplayerRoomUser user, BeatmapAvailability availability);

        /// <summary>
        /// Changes a room's state.
        /// </summary>
        /// <param name="room">The room.</param>
        /// <param name="newState">The new room state.</param>
        Task ChangeRoomState(ServerMultiplayerRoom room, MultiplayerRoomState newState);

        /// <summary>
        /// Starts a match in a room.
        /// </summary>
        /// <param name="room">The room to start the match for.</param>
        /// <exception cref="InvalidStateException">If the current playlist item is expired or the room is not in an <see cref="MultiplayerRoomState.Open"/> state.</exception>
        Task StartMatch(ServerMultiplayerRoom room);

        /// <summary>
        /// Should be called when user states change, to check whether the new overall room state can trigger a room-level state change.
        /// </summary>
        Task UpdateRoomStateIfRequired(ServerMultiplayerRoom room);

        Task NotifyMatchmakingItemSelected(ServerMultiplayerRoom room, int userId, long playlistItemId);

        Task NotifyMatchmakingItemDeselected(ServerMultiplayerRoom room, int userId, long playlistItemId);

        Task CheckVotesToSkipPassed(ServerMultiplayerRoom room);

        void Log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information);

        void Error(MultiplayerRoomUser? user, string message, Exception exception);
    }
}
