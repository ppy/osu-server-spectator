// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public interface IMatchmakingMatchController
    {
        public uint PoolId { get; set; }

        void SkipToNextStage(out Task countdownTask);
    }
}
