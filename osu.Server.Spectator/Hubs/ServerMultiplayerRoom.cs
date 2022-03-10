// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class ServerMultiplayerRoom : MultiplayerRoom
    {
        private readonly IMultiplayerServerMatchCallbacks hubCallbacks;

        private MatchTypeImplementation matchTypeImplementation;

        public MatchTypeImplementation MatchTypeImplementation
        {
            get => matchTypeImplementation;
            set
            {
                if (matchTypeImplementation == value)
                    return;

                matchTypeImplementation = value;

                foreach (var u in Users)
                    matchTypeImplementation.HandleUserJoined(u);
            }
        }

        public readonly MultiplayerQueue Queue;

        public ServerMultiplayerRoom(long roomId, IMultiplayerServerMatchCallbacks hubCallbacks)
            : base(roomId)
        {
            this.hubCallbacks = hubCallbacks;

            // just to ensure non-null.
            matchTypeImplementation = createTypeImplementation(MatchType.HeadToHead);
            Queue = new MultiplayerQueue(this, hubCallbacks);
        }

        public async Task Initialise(IDatabaseFactory dbFactory)
        {
            ChangeMatchType(Settings.MatchType);
            await Queue.Initialise(dbFactory);
        }

        public void ChangeMatchType(MatchType type) => MatchTypeImplementation = createTypeImplementation(type);

        public void AddUser(MultiplayerRoomUser user)
        {
            Users.Add(user);
            MatchTypeImplementation.HandleUserJoined(user);
        }

        public void RemoveUser(MultiplayerRoomUser user)
        {
            Users.Remove(user);
            MatchTypeImplementation.HandleUserLeft(user);
        }

        private MatchTypeImplementation createTypeImplementation(MatchType type)
        {
            switch (type)
            {
                case MatchType.TeamVersus:
                    return new TeamVersus(this, hubCallbacks);

                default:
                    return new HeadToHead(this, hubCallbacks);
            }
        }

        /// <summary>
        /// Cancelled when the countdown is cancelled.
        /// </summary>
        private CancellationTokenSource? countdownCancellationSource;

        /// <summary>
        /// Cancelled when the countdown is forcefully finished (<see cref="FinishCountdown"/>).
        /// </summary>
        private CancellationTokenSource? countdownFinishSource;

        private Task? countdownTask;

        /// <summary>
        /// Starts a new countdown.
        /// </summary>
        public async Task StartCountdown(TimeSpan duration, Func<Task> onFinished)
        {
            await CancelCountdown();

            countdownTask = Task.Run(async () =>
            {
                var cancellationSource = countdownCancellationSource = new CancellationTokenSource();
                var finishSource = countdownFinishSource = new CancellationTokenSource();
                var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token, finishSource.Token);

                try
                {
                    await Task.Delay(duration, linkedSource.Token);
                }
                catch (OperationCanceledException)
                {
                }

                try
                {
                    cancellationSource.Token.ThrowIfCancellationRequested();
                    await onFinished();
                }
                finally
                {
                    countdownCancellationSource = null;
                    countdownFinishSource = null;
                    countdownTask = null;
                    linkedSource.Dispose();
                }
            });
        }

        /// <summary>
        /// Cancels a countdown, preventing its continuation from running.
        /// </summary>
        public async Task CancelCountdown()
        {
            var source = countdownCancellationSource;
            source?.Cancel();

            try
            {
                var task = countdownTask;
                if (task != null)
                    await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Finishes a countdown, causing its continuation to run as soon as possible.
        /// </summary>
        public async Task FinishCountdown()
        {
            var source = countdownFinishSource;
            source?.Cancel();

            try
            {
                var task = countdownTask;
                if (task != null)
                    await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
