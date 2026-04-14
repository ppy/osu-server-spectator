// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Responsible for managing (creating and closing) multiplier rooms in response to user requests.
    /// </summary>
    public interface IMultiplayerRoomController
    {
        /// <summary>
        /// Creates a room with a single user in it.
        /// </summary>
        /// <param name="user">The user creating the room.</param>
        /// <param name="roomId">The ID of the room.</param>
        /// <param name="password">The password to the room.</param>
        /// <returns>Snapshot of the state of the created room.</returns>
        Task<MultiplayerRoom> CreateRoom(
            IMultiplayerUserState user,
            long roomId,
            string password);

        /// <summary>
        /// Joins a user to a room.
        /// </summary>
        /// <param name="user">The user joining the room.</param>
        /// <param name="roomId">The ID of the room.</param>
        /// <param name="password">The password to the room.</param>
        /// <returns>Snapshot of the state of the joined room.</returns>
        Task<MultiplayerRoom> JoinRoom(
            IMultiplayerUserState user,
            long roomId,
            string password);

        /// <summary>
        /// The given user leaves the room.
        /// </summary>
        /// <param name="user">The user leaving the room.</param>
        /// <param name="roomUsage">The room being left.</param>
        /// <param name="forceCloseOnEmpty">
        /// Whether to force the room to close if it is empty after the user has left.
        /// Only has an observable effect in rooms with <see cref="ServerMultiplayerRoom.TournamentMode"/> enabled,
        /// as tournament mode rooms are the only ones which are allowed to remain open while empty for 30 minutes as counted from the time when the last user leaves.
        /// </param>
        Task LeaveRoom(
            IMultiplayerUserState user,
            ItemUsage<ServerMultiplayerRoom> roomUsage,
            bool forceCloseOnEmpty = false);

        /// <summary>
        /// The given user is kicked from the room.
        /// Permissions for kicking are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="kickedUser">The user being kicked from the room.</param>
        /// <param name="roomUsage">The room which <paramref name="kickedUser"/> is being kicked from.</param>
        /// <param name="kickedBy">The ID of the user kicking the <paramref name="kickedUser"/>.</param>
        Task KickUserFromRoom(
            IMultiplayerUserState kickedUser,
            ItemUsage<ServerMultiplayerRoom> roomUsage,
            int kickedBy);

        /// <summary>
        /// The given user is banned from the room.
        /// If the user is currently in the room, they will be kicked.
        /// Additionally, the banned user will not be able to <see cref="JoinRoom"/> again, even with the correct credentials.
        /// Permissions for banning are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="bannedUserId">The ID of the user to ban.</param>
        /// <param name="roomUsage">The room which <paramref name="bannedUserId"/> is being banned from.</param>
        /// <param name="bannedBy">The ID of the user banning the <paramref name="bannedUserId"/></param>
        Task BanUserFromRoom(
            int bannedUserId,
            ItemUsage<ServerMultiplayerRoom> roomUsage,
            int bannedBy);

        /// <summary>
        /// Retrieves a <see cref="ServerMultiplayerRoom"/> usage for thread-safe operation.
        /// </summary>
        /// <param name="roomId">The ID of the room to retrieve.</param>
        Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId);

        /// <summary>
        /// Tries to retrieve a <see cref="ServerMultiplayerRoom"/> usage for thread-safe operation.
        /// Can return <see langword="null"/> if the room no longer exists.
        /// </summary>
        /// <remarks>
        /// Exposed for purposes of delayed operations (primarily countdowns) which cannot occur
        /// in the context of larger operations which should already ensure correct locking.
        /// Should not be used for any other purpose.
        /// </remarks>
        /// <param name="roomId">The ID of the room to retrieve.</param>
        Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId);
    }
}
