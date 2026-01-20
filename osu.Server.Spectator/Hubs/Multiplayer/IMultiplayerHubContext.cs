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
        /// Retrieves a <see cref="ServerMultiplayerRoom"/> usage.
        /// </summary>
        /// <param name="roomId">The ID of the room to retrieve.</param>
        Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId);

        void Log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information);

        void Error(MultiplayerRoomUser? user, string message, Exception exception);
    }
}
