// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// The implementation for a countdown timer.
    /// </summary>
    public class CountdownImplementation
    {
        private readonly long roomId;
        private readonly IMultiplayerServerMatchCallbacks hub;

        private CancellationTokenSource? countdownStopSource;
        private CancellationTokenSource? countdownFinishSource;
        private Task countdownTask = Task.CompletedTask;

        public CountdownImplementation(long roomId, IMultiplayerServerMatchCallbacks hub)
        {
            this.roomId = roomId;
            this.hub = hub;
        }

        /// <summary>
        /// Starts a new countdown, stopping any existing one.
        /// </summary>
        /// <param name="countdown">The countdown to start. The <see cref="MultiplayerRoom"/> will receive this object for the duration of the countdown.</param>
        /// <param name="onComplete">A callback to be invoked when the countdown completes.</param>
        public void Start(MultiplayerCountdown countdown, Func<ServerMultiplayerRoom, Task> onComplete)
        {
            Stop();

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
                using (var roomUsage = await hub.GetRoom(roomId))
                {
                    if (roomUsage.Item == null)
                        return;

                    // The countdown could have been cancelled in a separate request before this task was able to run.
                    if (stopSource.IsCancellationRequested)
                        return;

                    roomUsage.Item.Countdown = countdown;
                    await hub.SendMatchEvent(roomUsage.Item, new CountdownChangedEvent { Countdown = countdown });
                }

                // Run the countdown.
                try
                {
                    await Task.Delay(countdown.EndTime - DateTimeOffset.Now, cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Clients need to be notified of cancellations in the following code.
                }

                // Notify users that the countdown has finished (or cancelled) and run the continuation.
                using (var roomUsage = await hub.GetRoom(roomId))
                {
                    if (roomUsage.Item == null)
                        return;

                    roomUsage.Item.Countdown = null;
                    await hub.SendMatchEvent(roomUsage.Item, new CountdownChangedEvent { Countdown = null });

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
        public void Stop() => countdownStopSource?.Cancel();

        /// <summary>
        /// Forces the current countdown to finish and run its continuation as soon as possible.
        /// </summary>
        public void Finish() => countdownFinishSource?.Cancel();

        /// <summary>
        /// Whether a countdown is currently running.
        /// </summary>
        public bool IsRunning => !countdownTask.IsCompleted;
    }
}
