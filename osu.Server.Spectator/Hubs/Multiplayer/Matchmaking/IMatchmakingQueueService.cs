// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public interface IMatchmakingQueueService : IHostedService
    {
        /// <summary>
        /// Adds or removes a user from the matchmaking queue, depending on their current queued status.
        /// </summary>
        /// <param name="connectionId">The user connection.</param>
        Task AddOrRemoveFromQueueAsync(string connectionId);

        /// <summary>
        /// Definitively removes a user from the matchmaking queue.
        /// </summary>
        /// <param name="connectionId">The user connection.</param>
        Task RemoveFromQueueAsync(string connectionId);
    }
}
