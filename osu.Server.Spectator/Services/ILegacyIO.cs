// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Services
{
    public interface ILegacyIO
    {
        /// <summary>
        /// Creates an osu!web Room.
        /// </summary>
        /// <param name="userId">The ID of the user that wants to create the room.</param>
        /// <param name="room">The room.</param>
        /// <returns>The room's ID.</returns>
        Task<long> CreateRoom(int userId, MultiplayerRoom room);

        /// <summary>
        /// Joins an osu!web Room.
        /// </summary>
        /// <param name="roomId">The ID of the room to join.</param>
        /// <param name="userId">The ID of the user wanting to join the room.</param>
        Task JoinRoom(long roomId, int userId);

        /// <summary>
        /// Parts an osu!web Room.
        /// </summary>
        /// <param name="roomId">The ID of the room to part.</param>
        /// <param name="userId">The ID of the user wanting to part the room.</param>
        Task PartRoom(long roomId, int userId);
    }
}
