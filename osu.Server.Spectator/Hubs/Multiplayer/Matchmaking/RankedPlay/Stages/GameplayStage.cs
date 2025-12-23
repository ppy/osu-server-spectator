// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages
{
    public class GameplayStage : RankedPlayStageImplementation
    {
        public GameplayStage(RankedPlayMatchController controller)
            : base(controller)
        {
        }

        protected override RankedPlayStage Stage => RankedPlayStage.Gameplay;
        protected override TimeSpan Duration => TimeSpan.MaxValue;

        protected override async Task Begin()
        {
            await EventLogger.LogMatchmakingGameplayBeatmapAsync(Room.RoomID, Room.Settings.PlaylistItemId);
            await Hub.StartMatch(Room);
        }

        protected override async Task Finish()
        {
            await Controller.RemoveCards(State.ActiveUserId, [Room.Settings.PlaylistItemId]);
            await Controller.GotoStage(RankedPlayStage.Results);
        }

        public override async Task HandleGameplayCompleted()
        {
            await Finish();
        }
    }
}
