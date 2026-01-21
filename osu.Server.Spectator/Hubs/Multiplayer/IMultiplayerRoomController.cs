// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Responsible for managing (creating and closing) multiplier rooms in response to user requests.
    /// </summary>
    public interface IMultiplayerRoomController
    {
        /// <summary>
        /// Retrieves a <see cref="ServerMultiplayerRoom"/> usage for thread-safe operation.
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
