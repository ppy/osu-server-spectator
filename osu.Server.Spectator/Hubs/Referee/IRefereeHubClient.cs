// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Server.Spectator.Hubs.Referee.Models.Events;

namespace osu.Server.Spectator.Hubs.Referee
{
    /// <summary>
    /// Defines all messages that a server can send to the client.
    /// </summary>
    [PublicAPI]
    public interface IRefereeHubClient
    {
        /// <summary>
        /// A response to <see cref="IRefereeHubServer.Ping"/>.
        /// Used to test bidirectional connection.
        /// </summary>
        [Obsolete("Only exposed for testing purposes, will be removed when API reaches maturity. Do not use.")]
        Task Pong(string message);

        /// <summary>
        /// A user has joined a refereed room.
        /// </summary>
        Task UserJoined(UserJoinedEvent info);

        /// <summary>
        /// A user has left a refereed room.
        /// </summary>
        Task UserLeft(UserLeftEvent info);

        /// <summary>
        /// A user has been kicked from a refereed room.
        /// </summary>
        Task UserKicked(UserKickedEvent info);

        /// <summary>
        /// A room's settings have changed.
        /// </summary>
        Task RoomSettingsChanged(RoomSettingsChangedEvent info);

        /// <summary>
        /// A playlist item has been added to a room.
        /// </summary>
        Task PlaylistItemAdded(PlaylistItemAddedEvent info);

        /// <summary>
        /// A playlist item in a room has changed.
        /// </summary>
        Task PlaylistItemChanged(PlaylistItemChangedEvent info);

        /// <summary>
        /// A playlist item has been removed from a room.
        /// </summary>
        Task PlaylistItemRemoved(PlaylistItemRemovedEvent info);

        /// <summary>
        /// A user's status in a room has changed.
        /// </summary>
        Task UserStatusChanged(UserStatusChangedEvent info);

        /// <summary>
        /// A user's selected free mods in a room have changed.
        /// </summary>
        Task UserModsChanged(UserModsChangedEvent info);

        /// <summary>
        /// A user's selected style in a room has changed.
        /// </summary>
        Task UserStyleChanged(UserStyleChangedEvent info);

        /// <summary>
        /// A user's team in a room has changed.
        /// </summary>
        Task UserTeamChanged(UserTeamChangedEvent info);

        /// <summary>
        /// A countdown in a room has started.
        /// </summary>
        Task CountdownStarted(CountdownStartedEvent info);

        /// <summary>
        /// A countdown in a room has stopped.
        /// </summary>
        Task CountdownStopped(CountdownStoppedEvent info);

        /// <summary>
        /// A match in a room has started.
        /// </summary>
        Task MatchStarted(MatchStartedEvent info);

        /// <summary>
        /// A match in a room has been aborted.
        /// </summary>
        Task MatchAborted(MatchAbortedEvent info);

        /// <summary>
        /// A match in a room has completed.
        /// </summary>
        Task MatchCompleted(MatchCompletedEvent info);
    }
}
