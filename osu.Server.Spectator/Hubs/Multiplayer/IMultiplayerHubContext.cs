// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        Task ChangeUserVoteToSkipIntro(ServerMultiplayerRoom room, MultiplayerRoomUser user, bool voted);

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

        Task CheckVotesToSkipPassed(ServerMultiplayerRoom room);

        void Log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information);

        void Error(MultiplayerRoomUser? user, string message, Exception exception);
    }
}
