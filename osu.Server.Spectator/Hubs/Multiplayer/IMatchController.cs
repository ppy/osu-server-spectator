// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public interface IMatchController
    {
        MultiplayerPlaylistItem CurrentItem { get; }

        Task Initialise();

        Task HandleSettingsChanged();

        Task HandleGameplayCompleted();

        /// <summary>
        /// Called when a user has requested a match type specific action.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="request">The nature of the action.</param>
        Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request);

        /// <summary>
        /// Called once for each user which joins the room. Will be run once for each user after initial construction.
        /// </summary>
        /// <param name="user">The user which joined the room.</param>
        Task HandleUserJoined(MultiplayerRoomUser user);

        /// <summary>
        /// Called once for each user leaving the room.
        /// </summary>
        /// <param name="user">The user which left the room.</param>
        Task HandleUserLeft(MultiplayerRoomUser user);

        /// <summary>
        /// Add a playlist item to the room's queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="user">The user adding the item.</param>
        /// <exception cref="NotHostException">If the adding user is not the host in host-only mode.</exception>
        /// <exception cref="InvalidStateException">If the given playlist item is not valid.</exception>
        Task AddPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user);

        Task EditPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user);

        /// <summary>
        /// Removes a playlist item from the room's queue.
        /// </summary>
        /// <param name="playlistItemId">The item to remove.</param>
        /// <param name="user">The user removing the item.</param>
        Task RemovePlaylistItem(long playlistItemId, MultiplayerRoomUser user);

        MatchStartedEventDetail GetMatchDetails();
    }
}
