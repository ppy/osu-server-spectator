// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class WaitForJoinStage : RankedPlayStageImplementation
    {
        public WaitForJoinStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.WaitForJoin;
        protected override TimeSpan Duration => TimeSpan.FromSeconds(60);

        protected override Task Begin()
        {
            return Task.CompletedTask;
        }

        protected override async Task Finish()
        {
            if (Room.Users.Count == State.Users.Count)
                await Controller.GotoStage(RankedPlayStage.RoundWarmup);
            else
                await Controller.GotoStage(RankedPlayStage.Ended);
        }

        public override async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            // Populate the user's initial hand.
            await Controller.AddCards(user.UserID, RankedPlayMatchController.PLAYER_HAND_SIZE);

            if (Room.Users.Count == State.Users.Count)
                await Finish();
        }

        public override async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // Allow users to quit early without incurring a loss, but the match can no longer proceed.
            await CloseMatch();
        }
    }
}
