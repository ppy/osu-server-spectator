// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Services
{
    public interface ILegacyIO
    {
        /// <summary>
        /// Creates an osu!web room.
        /// </summary>
        /// <remarks>
        /// This does not join the creating user to the room. A subsequent call to <see cref="AddUserToRoomAsync"/> should be made if required.
        /// </remarks>
        /// <param name="userId">The ID of the user that wants to create the room.</param>
        /// <param name="room">The room.</param>
        /// <returns>The room's ID.</returns>
        Task<long> CreateRoomAsync(int userId, MultiplayerRoom room);

        /// <summary>
        /// Adds a user to an osu!web room.
        /// </summary>
        /// <remarks>
        /// This performs setup tasks like adding the user to the relevant chat channel.
        /// </remarks>
        /// <param name="roomId">The ID of the room to join.</param>
        /// <param name="userId">The ID of the user wanting to join the room.</param>
        Task AddUserToRoomAsync(long roomId, int userId);

        /// <summary>
        /// Parts an osu!web room.
        /// </summary>
        /// <remarks>
        /// This performs setup tasks like removing the user from any relevant chat channels.
        /// </remarks>
        /// <param name="roomId">The ID of the room to part.</param>
        /// <param name="userId">The ID of the user wanting to part the room.</param>
        Task RemoveUserFromRoomAsync(long roomId, int userId);
    }
}
