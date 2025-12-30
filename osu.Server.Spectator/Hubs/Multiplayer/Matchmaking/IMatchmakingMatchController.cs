// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public interface IMatchmakingMatchController
    {
        Task Initialise(uint poolId, MatchmakingQueueUser[] users);

        void SkipToNextStage(out Task countdownTask);
    }
}
