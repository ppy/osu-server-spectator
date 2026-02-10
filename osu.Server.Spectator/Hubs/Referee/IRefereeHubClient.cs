// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Server.Spectator.Hubs.Referee.Models.Events;

namespace osu.Server.Spectator.Hubs.Referee
{
    /// <summary>
    /// Defines all messages that a server can send to the client.
    /// </summary>
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
    }
}
