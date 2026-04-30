// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay
{
    public abstract class RankedPlayStageImplementation
    {
        /// <summary>
        /// The stage which this implementation implements.
        /// </summary>
        protected abstract RankedPlayStage Stage { get; }

        /// <summary>
        /// The duration of this stage, before <see cref="Finish"/> is invoked.
        /// </summary>
        protected abstract TimeSpan Duration { get; }

        protected ServerMultiplayerRoom Room => Controller.Room;
        protected RankedPlayRoomState State => Controller.State;
        protected IDatabaseFactory DbFactory => Controller.DbFactory;
        protected MultiplayerEventDispatcher EventDispatcher => Controller.EventDispatcher;

        protected readonly RankedPlayMatchController Controller;

        protected RankedPlayStageImplementation(RankedPlayMatchController controller)
        {
            Controller = controller;
        }

        /// <summary>
        /// Enters this stage.
        /// </summary>
        public async Task Enter()
        {
            State.Stage = Stage;

            await Begin();
            await EventDispatcher.PostMatchRoomStateChangedAsync(Room);

            await FinishWithCountdown(Duration);
        }

        /// <summary>
        /// Invokes <see cref="Finish"/> after a delay.
        /// </summary>
        protected async Task FinishWithCountdown(TimeSpan duration)
        {
            // The code below normally serves a second purpose to cancel any existing countdown.
            await Room.StopCountdown(Room.FindCountdownOfType<RankedPlayStageCountdown>());

            if (duration == TimeSpan.MaxValue)
                return;

            if (duration <= TimeSpan.Zero)
            {
                await Finish();
                return;
            }

            await Room.StartCountdown(new RankedPlayStageCountdown
            {
                Stage = Stage,
                TimeRemaining = duration
            }, async _ => await Finish());
        }

        /// <summary>
        /// Handles the initial actions when this stage is entered.
        /// </summary>
        protected abstract Task Begin();

        /// <summary>
        /// Handles any actions after the countdown timer runs out.
        /// </summary>
        protected abstract Task Finish();

        public virtual Task HandleUserJoined(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            await KillUser(user);
            await CloseMatch();
        }

        public virtual Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleGameplayCompleted()
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleDiscardCards(MultiplayerRoomUser user, RankedPlayCardItem[] cards)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandlePlayCard(MultiplayerRoomUser user, RankedPlayCardItem card)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleCardHandReplayRequest(MultiplayerRoomUser user, RankedPlayCardHandReplayRequest request)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Kills a user, reducing their life points to zero.
        /// </summary>
        protected async Task KillUser(MultiplayerRoomUser user)
        {
            if (Controller.Ranked)
                Controller.MatchmakingService.BanUser(user.UserID, TimeSpan.FromMinutes(10));

            State.Users[user.UserID].Life = 0;
            await EventDispatcher.PostMatchRoomStateChangedAsync(Room);

            if (!HasGameplayRoundsRemaining())
                await Controller.HandleMatchCompleted();
        }

        /// <summary>
        /// Transitions to the ended stage.
        /// </summary>
        protected async Task CloseMatch()
        {
            await Controller.GotoStage(RankedPlayStage.Ended);
        }

        /// <summary>
        /// Whether there are any more gameplay rounds that can be played.
        /// </summary>
        protected bool HasGameplayRoundsRemaining()
        {
            int countPlayersAlive = State.Users.Count(u => u.Value.Life > 0);
            int countCardsRemaining = Controller.DeckCount + State.Users.Sum(u => u.Value.Hand.Count);
            return countPlayersAlive > 1 && countCardsRemaining > 0;
        }
    }
}
