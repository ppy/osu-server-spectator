// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Hubs
{
    public interface IMultiplayerServerMatchCallbacks
    {
        /// <summary>
        /// Send an immediate event to all clients in a room.
        /// </summary>
        /// <remarks>
        /// This should be used for events which have no permanent effect on state.
        /// For operations which are intended to persist (and be visible to new users which join a room) use <see cref="UpdateMatchRoomState"/> or <see cref="UpdateMatchUserState"/> instead.
        /// </remarks>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="e">The event.</param>
        Task SendMatchEvent(MultiplayerRoom room, MatchServerEvent e);

        /// <summary>
        /// Let the hub know that the room's <see cref="MultiplayerRoom.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room whose state has changed.</param>
        Task UpdateMatchRoomState(MultiplayerRoom room);

        /// <summary>
        /// Let the hub know that the a user's <see cref="MultiplayerRoomUser.MatchState"/> has been altered.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="user">The user whose state has changed.</param>
        Task UpdateMatchUserState(MultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Let the hub know that a playlist item has been added.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The added item.</param>
        Task OnPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item);

        /// <summary>
        /// Let the hub know that a playlist item has been removed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="playlistItemId">The removed item.</param>
        Task OnPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId);

        /// <summary>
        /// Let the hub know that a playlist item has been changed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        /// <param name="item">The changed item.</param>
        Task OnPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item);

        /// <summary>
        /// Let the hub know that the room settings have been changed.
        /// </summary>
        /// <param name="room">The room to send the event to.</param>
        Task OnMatchSettingsChanged(ServerMultiplayerRoom room);
    }
}
