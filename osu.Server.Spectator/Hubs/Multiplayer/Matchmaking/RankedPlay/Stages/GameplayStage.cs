// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
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
            Debug.Assert(State.ActiveUserId != null);

            await EventDispatcher.PostPlayerBeatmapPickFinalised(Room.RoomID, State.ActiveUserId.Value, Room.Settings.PlaylistItemId);
            await ServerMultiplayerRoom.StartMatch(Room);
        }

        protected override async Task Finish()
        {
            Debug.Assert(State.ActiveUserId != null);
            Debug.Assert(Controller.LastActivatedCard != null);

            await Controller.RemoveCards(State.ActiveUserId.Value, [Controller.LastActivatedCard]);
            await Controller.GotoStage(RankedPlayStage.Results);
        }

        public override async Task HandleGameplayCompleted()
        {
            await Finish();
        }

        public override async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            // Kill the user but let the match continue to its natural conclusion (via the results stage).
            await KillUser(user);
        }
    }
}
