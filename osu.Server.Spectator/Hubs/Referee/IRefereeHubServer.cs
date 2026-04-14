// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Server.Spectator.Hubs.Referee.Models;
using osu.Server.Spectator.Hubs.Referee.Models.Requests;
using osu.Server.Spectator.Hubs.Referee.Models.Responses;

namespace osu.Server.Spectator.Hubs.Referee
{
    /// <summary>
    /// Defines all operations that a client can perform on the server.
    /// </summary>
    [PublicAPI]
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
        /// Corresponds to the <c>!mp make</c> command on bancho (with the slight adjustment of requiring a beatmap and ruleset).
        /// </summary>
        /// <remarks>
        /// Other than the supplied parameters, the room will use:
        /// <list type="bullet">
        /// <item>a random password,</item>
        /// <item><see cref="MatchType.HeadToHead"></see> match type,</item>
        /// <item><see cref="osu.Game.Online.Multiplayer.QueueMode.HostOnly">host-only</see> queue mode,</item>
        /// <item>automatic intro skip enabled</item>
        /// </list>
        /// by default.
        /// </remarks>
        Task<RoomJoinedResponse> MakeRoom(MakeRoomRequest request);

        /// <summary>
        /// Joins an existing multiplayer room with the given <paramref name="roomId"/>.
        /// The user must already be an added referee of this room.
        /// The password is not required.
        /// </summary>
        Task<RoomJoinedResponse> JoinRoom(long roomId);

        /// <summary>
        /// Closes the room with the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp close</c> command on bancho.
        /// </summary>
        Task CloseRoom(long roomId);

        /// <summary>
        /// Invites the player with the given <paramref name="userId"/> to the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp invite</c> command on bancho.
        /// Note that success of this operation does **not** confirm receipt of the invite, just a lack of errors when sending it.
        /// </summary>
        Task InvitePlayer(long roomId, int userId);

        /// <summary>
        /// Kicks the player with the given <paramref name="userId"/> from the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp kick</c> command on bancho.
        /// </summary>
        Task KickPlayer(long roomId, int userId);

        /// <summary>
        /// Bans the user with the given <paramref name="bannedUserId"/> from the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp ban</c> command on bancho.
        /// </summary>
        Task BanUser(long roomId, int bannedUserId);

        /// <summary>
        /// Adds the user with the given <paramref name="targetUserId"/> as a referee of the room with the given <paramref name="roomId"/>.
        /// The user has to call <see cref="JoinRoom"/> to start performing referee actions.
        /// Only the room host can call this method, and they cannot call it on themselves.
        /// </summary>
        Task AddReferee(long roomId, int targetUserId);

        /// <summary>
        /// Removes the user with the given <paramref name="targetUserId"/> from the set of referees of the room with the given <paramref name="roomId"/>.
        /// If the user was joined to the room at the time of this call, they will be kicked from the room.
        /// Only the room host can call this method, and they cannot call it on themselves.
        /// </summary>
        Task RemoveReferee(long roomId, int targetUserId);

        /// <summary>
        /// Lists all rooms that the caller is a referee in.
        /// </summary>
        Task<ListRoomsResponse> ListRooms();

        /// <summary>
        /// Changes the settings of the room with the given <paramref name="roomId"/>.
        /// Encompasses the <c>!mp name</c>, <c>!mp password</c>, and <c>!mp set</c> commands on bancho.
        /// </summary>
        Task ChangeRoomSettings(long roomId, ChangeRoomSettingsRequest request);

        /// <summary>
        /// Edits the current playlist item in the room with the given <paramref name="roomId"/>.
        /// Encompasses the <c>!mp map</c> and <c>!mp mods</c> commands on bancho.
        /// </summary>
        Task EditCurrentPlaylistItem(long roomId, EditCurrentPlaylistItemRequest request);

        /// <summary>
        /// <para>
        /// Adds a playlist item to the room.
        /// </para>
        /// <para>
        /// The use of this method is generally not required; multiplayer rooms always ensure at least one item exists
        /// and you can use <see cref="EditCurrentPlaylistItem"/> at any point (except for when a match is in progress) to modify it.
        /// The best use of this method is for managing multiple queued-up predetermined items in succession.
        /// </para>
        /// </summary>
        Task AddPlaylistItem(long roomId, AddPlaylistItemRequest request);

        /// <summary>
        /// <para>
        /// Edits a playlist item in the room.
        /// </para>
        /// <para>
        /// To edit the current playlist item, you can use the <see cref="EditCurrentPlaylistItem"/> shorthand.
        /// The best use of this method is for queueing up multiple predetermined items in succession.
        /// </para>
        /// </summary>
        Task EditPlaylistItem(long roomId, EditPlaylistItemRequest request);

        /// <summary>
        /// Removes a playlist item from the room.
        /// </summary>
        /// <para>
        /// The use of this method is generally not required; multiplayer rooms always ensure at least one item exists
        /// and you can use <see cref="EditCurrentPlaylistItem"/> at any point (except for when a match is in progress) to modify it.
        /// The best use of this method is for managing multiple queued-up predetermined items in succession.
        /// </para>
        Task RemovePlaylistItem(long roomId, RemovePlaylistItemRequest request);

        /// <summary>
        /// Initiates a random roll in the room.
        /// Corresponds to the <c>!roll</c> command on bancho.
        /// </summary>
        Task Roll(long roomId, RollRequest? request);

        /// <summary>
        /// Moves the user to a different team in the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp move</c> command on bancho.
        /// </summary>
        Task MoveUser(long roomId, MoveUserRequest request);

        /// <summary>
        /// Toggles players' ability to change teams in the room.
        /// Corresponds to the <c>!mp lock</c> and <c>!mp unlock</c> commands on bancho.
        /// </summary>
        // TODO: mention slots too once that's implemented
        Task SetLockState(long roomId, SetLockStateRequest request);

        /// <summary>
        /// Starts a match (immediately or with a countdown) in the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp start</c> command on bancho.
        /// </summary>
        Task StartMatch(long roomId, StartGameplayRequest request);

        /// <summary>
        /// Stops an ongoing match start countdown in the room with the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp aborttimer</c> command on bancho.
        /// </summary>
        Task StopMatchCountdown(long roomId);

        /// <summary>
        /// Aborts an ongoing match in the room with the given <paramref name="roomId"/>.
        /// Corresponds to the <c>!mp abort</c> command on bancho.
        /// </summary>
        Task AbortMatch(long roomId);
    }
}
