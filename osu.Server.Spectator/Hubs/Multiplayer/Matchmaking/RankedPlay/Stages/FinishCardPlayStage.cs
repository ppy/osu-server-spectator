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
    public class FinishCardPlayStage : RankedPlayStageImplementation
    {
        private bool userStatesReset;

        public FinishCardPlayStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.FinishCardPlay;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            // Reset ready states.
            await Room.HandleSettingsChanged(true);

            // HandleSettingsChanged(true) internally invokes separate events for user state and beatmap availability changes,
            // which trigger HandleUserStateChanged() in advance of when we want it to actually occur.
            userStatesReset = true;

            await continueWhenAllPlayersReady();
        }

        protected override async Task Finish()
        {
            await Controller.GotoStage(RankedPlayStage.GameplayWarmup);
        }

        public override async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            if (userStatesReset)
                await continueWhenAllPlayersReady();
        }

        private async Task continueWhenAllPlayersReady()
        {
            // Only require players to have the beatmap, but not necessarily have it loaded yet.
            if (Room.Users.All(u => u.BeatmapAvailability.State == DownloadState.LocallyAvailable))
                await Finish();
        }
    }
}
