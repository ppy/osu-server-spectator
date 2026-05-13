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

            if (Room.Users.All(isPlayerReady))
                await Controller.GotoStage(RankedPlayStage.GameplayWarmup);
            else
            {
                if (Room.Users.Any(isPlayerReady))
                {
                    foreach (var player in Room.Users.Where(u => !isPlayerReady(u)))
                        Controller.Damage(player.UserID, 100_000, State.DamageMultiplier);
                }

                await Controller.RemoveCards(State.ActiveUserId.Value, [Controller.LastActivatedCard]);

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
            if (Room.Users.All(isPlayerReady))
                await Finish();
        }

        /// <summary>
        /// Only requires players to have the beatmap, but not necessarily have it loaded yet.
        /// </summary>
        private bool isPlayerReady(MultiplayerRoomUser user)
            => user.BeatmapAvailability.State == DownloadState.LocallyAvailable;
    }
}
