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
    public class GameplayWarmupStage : RankedPlayStageImplementation
    {
        public GameplayWarmupStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.GameplayWarmup;
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
                await Controller.GotoStage(RankedPlayStage.Gameplay);
            else
            {
                await Controller.RemoveCards(State.ActiveUserId.Value, [Controller.LastActivatedCard]);

                // Subtract 100K HP from every player that failed to load the beatmap in time.
                // Although this seems unfair, it means that players are not able to purposefully block the others' picks.
                foreach (var player in Room.Users.Where(p => p.BeatmapAvailability.State != DownloadState.LocallyAvailable || p.State != MultiplayerUserState.Ready))
                    State.Users[player.UserID].Life = Math.Max(0, State.Users[player.UserID].Life - 100_000);

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
                await FinishWithCountdown(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Requires all players to be in the ready state, signaling they have finished viewing the beatmap details/etc.
        /// </summary>
        private bool allPlayersReady()
            => Room.Users.All(u => u.BeatmapAvailability.State == DownloadState.LocallyAvailable && u.State == MultiplayerUserState.Ready);
    }
}
