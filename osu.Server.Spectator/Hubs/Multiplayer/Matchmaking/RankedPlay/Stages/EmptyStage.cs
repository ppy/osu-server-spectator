// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class EmptyStage : RankedPlayStageImplementation
    {
        public EmptyStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.WaitForJoin;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override Task Begin()
        {
            return Task.CompletedTask;
        }

        protected override Task Finish()
        {
            return Task.CompletedTask;
        }
    }
}
