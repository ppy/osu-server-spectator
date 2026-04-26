// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class FinishCardPlayStage : RankedPlayStageImplementation
    {
        public FinishCardPlayStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.FinishCardPlay;
        protected override TimeSpan Duration => TimeSpan.FromMinutes(2);

        protected override async Task Begin()
        {
            await continueWhenAllPlayersReady();
        }

        protected override async Task Finish()
        {
            Debug.Assert(State.ActiveUserId != null);
            Debug.Assert(Controller.LastActivatedCard != null);

            if (allPlayersReady())
                await Controller.GotoStage(RankedPlayStage.GameplayWarmup);
            else
            {
                await Controller.RemoveCards(State.ActiveUserId.Value, [Controller.LastActivatedCard]);

                // Subtract 100K HP from every player that failed to load the beatmap in time.
                // Although this seems unfair, it means that players are not able to purposefully block the others' picks.
                foreach (var player in Room.Users.Where(p => p.BeatmapAvailability.State != DownloadState.LocallyAvailable))
                    Controller.Damage(player.UserID, 100_000);

                if (HasGameplayRoundsRemaining())
                    await Controller.GotoStage(RankedPlayStage.RoundWarmup);
                else
                    await Controller.GotoStage(RankedPlayStage.Ended);
            }
        }

        public override async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            await continueWhenAllPlayersReady();
        }

        private async Task continueWhenAllPlayersReady()
        {
            if (allPlayersReady())
                await Finish();
        }

        /// <summary>
        /// Only requires players to have the beatmap, but not necessarily have it loaded yet.
        /// </summary>
        private bool allPlayersReady()
            => Room.Users.All(u => u.BeatmapAvailability.State == DownloadState.LocallyAvailable);
    }
}
