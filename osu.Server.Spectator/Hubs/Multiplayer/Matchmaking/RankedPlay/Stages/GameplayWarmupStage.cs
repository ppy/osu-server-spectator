// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class GameplayWarmupStage : RankedPlayStageImplementation
    {
        public GameplayWarmupStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.GameplayWarmup;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            await continueWhenAllPlayersReady();
        }

        protected override async Task Finish()
        {
            await Controller.GotoStage(RankedPlayStage.Gameplay);
        }

        public override async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            await continueWhenAllPlayersReady();
        }

        private async Task continueWhenAllPlayersReady()
        {
            // Require players to be in the ready state, signaling they have finished viewing the beatmap details/etc.
            if (Room.Users.All(u => u.BeatmapAvailability.State == DownloadState.LocallyAvailable && u.State == MultiplayerUserState.Ready))
                await FinishWithCountdown(TimeSpan.FromSeconds(10));
        }
    }
}
