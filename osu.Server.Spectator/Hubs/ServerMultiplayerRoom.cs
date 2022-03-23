// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
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

        /// <summary>
        /// Ensures that all states in this <see cref="ServerMultiplayerRoom"/> are valid to be newly serialised out to a client.
        /// </summary>
        public void UpdateForRetrieval()
        {
            if (Countdown != null)
            {
                DateTimeOffset countdownEnd = countdownStartTime + countdownDuration;
                TimeSpan timeRemaining = countdownEnd - DateTimeOffset.Now;

                Countdown.TimeRemaining = timeRemaining.TotalSeconds > 0 ? timeRemaining : TimeSpan.Zero;
            }
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

        private CancellationTokenSource? countdownStopSource;
        private CancellationTokenSource? countdownFinishSource;
        private Task countdownTask = Task.CompletedTask;
        private TimeSpan countdownDuration;
        private DateTimeOffset countdownStartTime;

        /// <summary>
        /// Starts a new countdown, stopping any existing one.
        /// </summary>
        /// <param name="countdown">The countdown to start. The <see cref="MultiplayerRoom"/> will receive this object for the duration of the countdown.</param>
        /// <param name="onComplete">A callback to be invoked when the countdown completes.</param>
        public void StartCountdown(MultiplayerCountdown countdown, Func<ServerMultiplayerRoom, Task> onComplete)
        {
            StopCountdown();

            var stopSource = countdownStopSource = new CancellationTokenSource();
            var finishSource = countdownFinishSource = new CancellationTokenSource();
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(stopSource.Token, finishSource.Token);

            Task lastCountdownTask = countdownTask;
            countdownTask = start();

            // Some sections in the following code require single-threaded execution mode. This is achieved by re-retrieving the room from the hub, forcing an exclusive lock on it.
            async Task start()
            {
                // Wait for the last countdown to finalise before starting a new one.
                try
                {
                    await lastCountdownTask;
                }
                catch
                {
                    // Any failures in the last countdown should not prevent future countdowns from running.
                }

                // Notify users that a new countdown has started.
                // Note: The room must be re-retrieved rather than using our own instance to enforce single-thread access.
                using (var roomUsage = await hubCallbacks.GetRoom(RoomID))
                {
                    if (roomUsage.Item == null)
                        return;

                    // The countdown could have been cancelled in a separate request before this task was able to run.
                    if (stopSource.IsCancellationRequested)
                        return;

                    roomUsage.Item.Countdown = countdown;

                    countdownStartTime = DateTimeOffset.Now;
                    countdownDuration = countdown.TimeRemaining;

                    await hubCallbacks.SendMatchEvent(roomUsage.Item, new CountdownChangedEvent { Countdown = countdown });
                }

                // Run the countdown.
                try
                {
                    await Task.Delay(countdownDuration, cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Clients need to be notified of cancellations in the following code.
                }

                // Notify users that the countdown has finished (or cancelled) and run the continuation.
                // Note: The room must be re-retrieved rather than using our own instance to enforce single-thread access.
                using (var roomUsage = await hubCallbacks.GetRoom(RoomID))
                {
                    if (roomUsage.Item == null)
                        return;

                    roomUsage.Item.Countdown = null;

                    await hubCallbacks.SendMatchEvent(roomUsage.Item, new CountdownChangedEvent { Countdown = null });

                    using (cancellationSource)
                    {
                        if (stopSource.Token.IsCancellationRequested)
                            return;
                    }

                    // The continuation could be run outside of the room lock, however it seems saner to run it within the same lock as the cancellation token usage.
                    // Furthermore, providing a room-id instead of the room becomes cumbersome for usages, so this also provides a nicer API.
                    await onComplete(roomUsage.Item);
                }
            }
        }

        /// <summary>
        /// Stops the current countdown. Its continuation will not run.
        /// </summary>
        public void StopCountdown() => countdownStopSource?.Cancel();

        /// <summary>
        /// Forces the current countdown to finish and run its continuation as soon as possible.
        /// </summary>
        public void FinishCountdown() => countdownFinishSource?.Cancel();

        /// <summary>
        /// Whether a countdown is currently running.
        /// </summary>
        public bool IsCountdownRunning => !countdownTask.IsCompleted;
    }
}
