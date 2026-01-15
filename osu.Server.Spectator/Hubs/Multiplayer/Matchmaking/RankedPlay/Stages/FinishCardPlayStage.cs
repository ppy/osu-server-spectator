// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class FinishCardPlayStage : RankedPlayStageImplementation
    {
        public FinishCardPlayStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.FinishCardPlay;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            // Reset ready states.
            await Hub.NotifySettingsChanged(Room, true);
        }

        protected override async Task Finish()
        {
            await Controller.GotoStage(RankedPlayStage.GameplayWarmup);
        }

        public override async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            if (Room.Users.All(u => u.IsReadyForGameplay()))
                await FinishWithCountdown(TimeSpan.FromSeconds(3));
        }
    }
}
