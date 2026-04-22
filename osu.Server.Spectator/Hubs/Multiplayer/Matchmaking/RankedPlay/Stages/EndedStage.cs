// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class EndedStage : RankedPlayStageImplementation
    {
        public EndedStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.Ended;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            await Controller.HandleMatchCompleted();
        }

        protected override Task Finish()
        {
            return Task.CompletedTask;
        }

        public override Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // The match is over, no need to kill users.
            return Task.CompletedTask;
        }
    }
}
