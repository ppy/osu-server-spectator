// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Hubs.Referee.Models.Responses;

namespace osu.Server.Spectator.Hubs.Referee
{
    /// <summary>
    /// Defines all operations that a client can perform via the server.
    /// </summary>
    public interface IRefereeHubServer
    {
        /// <summary>
        /// Sends a textual message to the server.
        /// The server will respond with a <see cref="IRefereeHubClient.Pong"/>.
        /// Used to test bidirectional connection.
        /// </summary>
        [Obsolete("Only exposed for testing purposes, will be removed when API reaches maturity. Do not use.")]
        Task Ping(string message);

        /// <summary>
        /// Makes a new multiplayer room.
        /// Other than the supplied parameters, the room will use:
        /// <list type="bullet">
        /// <item>a random password,</item>
        /// <item><see cref="MatchType.HeadToHead"/> match type,</item>
        /// <item><see cref="QueueMode.HostOnly"/> queue mode,</item>
        /// <item>automatic intro skip enabled</item>
        /// </list>
        /// by default.
        /// </summary>
        /// <param name="rulesetId">The ID of the ruleset to play.</param>
        /// <param name="beatmapId">The ID of the beatmap to play.</param>
        /// <param name="roomName">The name of the room to create.</param>
        /// <returns></returns>
        Task<RoomJoinedResponse> MakeRoom(int rulesetId, int beatmapId, string roomName);

        /// <summary>
        /// Joins an existing multiplayer room with the given <paramref name="roomId"/>.
        /// The user must already be an added referee of this room.
        /// The password is not required.
        /// </summary>
        Task<RoomJoinedResponse> JoinRoom(long roomId);

        /// <summary>
        /// Leaves the multiplayer room with the given <paramref name="roomId"/>.
        /// This operation removes the caller's referee privileges;
        /// they will not be able to <see cref="JoinRoom"/> again unless granted referee privileges again by another referee in the room.
        /// </summary>
        Task LeaveRoom(long roomId);

        /// <summary>
        /// Closes the room with the given <paramref name="roomId"/>.
        /// </summary>
        Task CloseRoom(long roomId);

        /// <summary>
        /// Invites the player with the given <paramref name="userId"/> to the given <paramref name="roomId"/>.
        /// </summary>
        Task InvitePlayer(long roomId, int userId);

        /// <summary>
        /// Kicks the player with the given <paramref name="userId"/> from the given <paramref name="roomId"/>.
        /// </summary>
        Task KickPlayer(long roomId, int userId);
    }
}
