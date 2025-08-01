// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public interface IMatchmakingQueueProcessor : IHostedService
    {
        Task AddToQueueAsync(string connectionId);
        Task RemoveFromQueueAsync(string connectionId);
    }
}
