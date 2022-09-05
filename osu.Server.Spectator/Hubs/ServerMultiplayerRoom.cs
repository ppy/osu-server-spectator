// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly IMultiplayerHubContext hub;

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

        public ServerMultiplayerRoom(long roomId, IMultiplayerHubContext hub)
            : base(roomId)
        {
            this.hub = hub;

            // just to ensure non-null.
            matchTypeImplementation = createTypeImplementation(MatchType.HeadToHead);
            Queue = new MultiplayerQueue(this, hub);
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
            foreach (var countdown in ActiveCountdowns)
            {
                var info = countdownInfo[countdown];

                DateTimeOffset countdownEnd = info.StartTime + info.Duration;
                TimeSpan timeRemaining = countdownEnd - DateTimeOffset.Now;

                countdown.TimeRemaining = timeRemaining.TotalSeconds > 0 ? timeRemaining : TimeSpan.Zero;
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
                    return new TeamVersus(this, hub);

                default:
                    return new HeadToHead(this, hub);
            }
        }

        #region Countdowns

        private int nextCountdownId;
        private readonly Dictionary<MultiplayerCountdown, CountdownInfo> countdownInfo = new Dictionary<MultiplayerCountdown, CountdownInfo>();

        /// <summary>
        /// Starts a new countdown.
        /// </summary>
        /// <param name="countdown">The countdown to start. The <see cref="MultiplayerRoom"/> will receive this object for the duration of the countdown.</param>
        /// <param name="onComplete">A callback to be invoked when the countdown completes.</param>
        public async Task StartCountdown(MultiplayerCountdown countdown, Func<ServerMultiplayerRoom, Task> onComplete)
        {
            countdown.ID = nextCountdownId++;

            CountdownInfo info = new CountdownInfo(countdown);
            countdownInfo[countdown] = info;

            ActiveCountdowns.Add(countdown);

            await hub.NotifyNewMatchEvent(this, new CountdownStartedEvent(countdown));

            info.Task = start();

            async Task start()
            {
                // Run the countdown.
                try
                {
                    using (var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(info.StopSource.Token, info.SkipSource.Token))
                        await Task.Delay(info.Duration, cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Clients need to be notified of cancellations in the following code.
                }

                // Notify users that the countdown has finished (or cancelled) and run the continuation.
                // Note: The room must be re-retrieved rather than using our own instance to enforce single-thread access.
                using (var roomUsage = await hub.GetRoom(RoomID))
                {
                    try
                    {
                        if (roomUsage.Item == null)
                            return;

                        if (info.StopSource.IsCancellationRequested)
                            return;

                        Debug.Assert(countdownInfo.ContainsKey(countdown));
                        Debug.Assert(ActiveCountdowns.Contains(countdown));

                        await StopCountdown(countdown);

                        // The continuation could be run outside of the room lock, however it seems saner to run it within the same lock as the cancellation token usage.
                        // Furthermore, providing a room-id instead of the room becomes cumbersome for usages, so this also provides a nicer API.
                        await onComplete(roomUsage.Item);
                    }
                    finally
                    {
                        info.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Stops all countdowns of the given type, preventing their callbacks from running.
        /// </summary>
        /// <typeparam name="T">The countdown type.</typeparam>
        public async Task StopAllCountdowns<T>()
            where T : MultiplayerCountdown
        {
            foreach (var countdown in ActiveCountdowns.OfType<T>().ToArray())
                await StopCountdown(countdown);
        }

        /// <summary>
        /// Stops the given countdown, preventing its callback from running.
        /// </summary>
        /// <param name="countdown">The countdown to stop.</param>
        public async Task StopCountdown(MultiplayerCountdown countdown)
        {
            if (!countdownInfo.TryGetValue(countdown, out CountdownInfo? data))
                return;

            data.StopSource.Cancel();

            countdownInfo.Remove(countdown);
            ActiveCountdowns.Remove(data.Countdown);

            await hub.NotifyNewMatchEvent(this, new CountdownStoppedEvent(data.Countdown.ID));
        }

        /// <summary>
        /// Skips to the end of the given countdown and runs its callback (e.g. to start the match) as soon as possible unless the countdown has been cancelled.
        /// </summary>
        /// <param name="countdown">The countdown.</param>
        /// <returns>
        /// A task which will become completed when the active countdown completes. Make sure to await this *outside* a usage.
        /// </returns>
        public Task SkipToEndOfCountdown(MultiplayerCountdown? countdown)
        {
            if (countdown == null || !countdownInfo.TryGetValue(countdown, out CountdownInfo? info))
                return Task.CompletedTask;

            info.SkipSource.Cancel();
            return info.Task;
        }

        /// <summary>
        /// Retrieves the task for the given countdown, if one is running.
        /// </summary>
        /// <param name="countdown">The countdown to retrieve the task of.</param>
        public Task GetCountdownTask(MultiplayerCountdown? countdown)
            => countdown == null || !countdownInfo.TryGetValue(countdown, out CountdownInfo? info) ? Task.CompletedTask : info.Task;

        /// <summary>
        /// Searches the currently active countdowns and retrieves one of the given type.
        /// </summary>
        /// <typeparam name="T">The countdown type.</typeparam>
        /// <returns>A countdown of the given type, or null if no such countdown is running.</returns>
        public T? FindCountdownOfType<T>() where T : MultiplayerCountdown
            => ActiveCountdowns.OfType<T>().FirstOrDefault();

        /// <summary>
        /// Searches the currently active countdowns and retrieves the one matching a given ID.
        /// </summary>
        /// <param name="countdownId">The countdown ID.</param>
        /// <returns>The countdown matching the given ID, or null if no such countdown is running.</returns>
        public MultiplayerCountdown? FindCountdownById(int countdownId)
            => ActiveCountdowns.SingleOrDefault(c => c.ID == countdownId);

        private class CountdownInfo : IDisposable
        {
            public readonly MultiplayerCountdown Countdown;
            public readonly CancellationTokenSource StopSource = new CancellationTokenSource();
            public readonly CancellationTokenSource SkipSource = new CancellationTokenSource();
            public readonly DateTimeOffset StartTime = DateTimeOffset.Now;
            public readonly TimeSpan Duration;

            public Task Task { get; set; } = null!;

            public CountdownInfo(MultiplayerCountdown countdown)
            {
                Countdown = countdown;
                Duration = countdown.TimeRemaining;
            }

            public void Dispose()
            {
                StopSource.Dispose();
                SkipSource.Dispose();
            }
        }

        #endregion
    }
}
