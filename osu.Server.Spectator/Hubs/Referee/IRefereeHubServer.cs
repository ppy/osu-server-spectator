// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Hubs.Referee.Models.Requests;
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
        Task<RoomJoinedResponse> MakeRoom(MakeRoomRequest request);

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

        /// <summary>
        /// Changes the settings of the room with the given <paramref name="roomId"/>.
        /// </summary>
        Task ChangeRoomSettings(long roomId, ChangeRoomSettingsRequest request);

        /// <summary>
        /// Edits the current playlist item in the room with the given <paramref name="roomId"/>.
        /// </summary>
        Task EditCurrentPlaylistItem(long roomId, EditCurrentPlaylistItemRequest request);

        /// <summary>
        /// Moves the user to a different team in the given <paramref name="roomId"/>.
        /// </summary>
        Task MoveUser(long roomId, MoveUserRequest request);

        /// <summary>
        /// Starts a match (immediately or with a countdown) in the given <paramref name="roomId"/>.
        /// </summary>
        Task StartMatch(long roomId, StartGameplayRequest request);

        /// <summary>
        /// Stops an ongoing match start countdown in the room with the given <paramref name="roomId"/>.
        /// </summary>
        Task StopMatchCountdown(long roomId);

        /// <summary>
        /// Aborts an ongoing match in the room with the given <paramref name="roomId"/>.
        /// </summary>
        Task AbortMatch(long roomId);
    }
}
