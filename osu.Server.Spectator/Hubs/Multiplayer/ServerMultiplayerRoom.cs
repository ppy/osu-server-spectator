// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Hubs.Multiplayer.Standard;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class ServerMultiplayerRoom : MultiplayerRoom
    {
        public IMatchController Controller
        {
            get => matchController ?? throw new InvalidOperationException("Room not initialised.");
            private set => matchController = value;
        }

        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private IMatchController? matchController;

        private ServerMultiplayerRoom(long roomId, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventDispatcher eventDispatcher)
            : base(roomId)
        {
            this.hub = hub;
            this.dbFactory = dbFactory;
            this.eventDispatcher = eventDispatcher;
        }

        /// <summary>
        /// Attempt to retrieve and construct a room from the database backend, based on a room ID specification.
        /// This will check the database backing to ensure things are in a consistent state.
        /// This will also mark the room as active, indicating that this server is now in control of the room's lifetime.
        /// </summary>
        /// <param name="roomId">The room identifier.</param>
        /// <param name="hub">The multiplayer hub context.</param>
        /// <param name="dbFactory">The database factory.</param>
        /// <param name="eventDispatcher">Dispatcher responsible to relaying room events to applicable listeners.</param>
        /// <exception cref="InvalidOperationException">If the room does not exist in the database.</exception>
        /// <exception cref="InvalidStateException">If the match has already ended.</exception>
        public static async Task<ServerMultiplayerRoom> InitialiseAsync(long roomId, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventDispatcher eventDispatcher)
        {
            ServerMultiplayerRoom room = new ServerMultiplayerRoom(roomId, hub, dbFactory, eventDispatcher);

            // TODO: this call should be transactional, and mark the room as managed by this server instance.
            // This will allow for other instances to know not to reinitialise the room if the host arrives there.
            // Alternatively, we can move lobby retrieval away from osu-web and not require this in the first place.
            // Needs further discussion and consideration either way.
            using (var db = dbFactory.GetInstance())
            {
                hub.Log(room, null, $"Retrieving room {roomId} from database");
                var databaseRoom = await db.GetRealtimeRoomAsync(roomId);

                if (databaseRoom == null)
                    throw new InvalidOperationException("Specified match does not exist.");

                if (databaseRoom.ends_at != null && databaseRoom.ends_at < DateTimeOffset.Now)
                    throw new InvalidStateException("Match has already ended.");

                room.ChannelID = databaseRoom.channel_id;
                room.Settings = new MultiplayerRoomSettings
                {
                    Name = databaseRoom.name,
                    Password = databaseRoom.password,
                    MatchType = databaseRoom.type.ToMatchType(),
                    QueueMode = databaseRoom.queue_mode.ToQueueMode(),
                    AutoStartDuration = TimeSpan.FromSeconds(databaseRoom.auto_start_duration),
                    AutoSkip = databaseRoom.auto_skip
                };

                foreach (var item in await db.GetAllPlaylistItemsAsync(roomId))
                    room.Playlist.Add(item.ToMultiplayerPlaylistItem());

                await room.ChangeMatchType(room.Settings.MatchType);

                hub.Log(room, null, "Marking room active");
                await db.MarkRoomActiveAsync(room);
            }

            return room;
        }

        /// <summary>
        /// Ensures that all states in this <see cref="ServerMultiplayerRoom"/> are valid to be newly serialised out to a client.
        /// </summary>
        public void UpdateForRetrieval()
        {
            foreach (var countdown in ActiveCountdowns)
            {
                var countdownInfo = trackedCountdowns[countdown];

                DateTimeOffset countdownEnd = countdownInfo.StartTime + countdownInfo.Duration;
                TimeSpan timeRemaining = countdownEnd - DateTimeOffset.Now;

                countdown.TimeRemaining = timeRemaining.TotalSeconds > 0 ? timeRemaining : TimeSpan.Zero;
            }
        }

        [MemberNotNull(nameof(Controller))]
        public Task ChangeMatchType(MatchType type)
        {
            switch (type)
            {
                case MatchType.Matchmaking:
                    return ChangeMatchType(new MatchmakingMatchController(this, hub, dbFactory, eventDispatcher));

                case MatchType.TeamVersus:
                    return ChangeMatchType(new TeamVersusMatchController(this, hub, dbFactory, eventDispatcher));

                default:
                    return ChangeMatchType(new HeadToHeadMatchController(this, hub, dbFactory, eventDispatcher));
            }
        }

        [MemberNotNull(nameof(Controller))]
        public async Task ChangeMatchType(IMatchController controller)
        {
            Controller = controller;

            await Controller.Initialise();

            foreach (var u in Users)
                await Controller.HandleUserJoined(u);
        }

        public async Task AddUser(MultiplayerRoomUser user)
        {
            Users.Add(user);
            await Controller.HandleUserJoined(user);
        }

        public async Task RemoveUser(MultiplayerRoomUser user)
        {
            Users.Remove(user);
            await Controller.HandleUserLeft(user);
            await hub.CheckVotesToSkipPassed(this);
        }

        #region Countdowns

        private int nextCountdownId;
        private readonly Dictionary<MultiplayerCountdown, CountdownInfo> trackedCountdowns = new Dictionary<MultiplayerCountdown, CountdownInfo>();

        /// <summary>
        /// Starts a new countdown.
        /// </summary>
        /// <param name="countdown">The countdown to start. The <see cref="MultiplayerRoom"/> will receive this object for the duration of the countdown.</param>
        /// <param name="onComplete">A callback to be invoked when the countdown completes.</param>
        public async Task StartCountdown<T>(T countdown, Func<ServerMultiplayerRoom, Task>? onComplete = null)
            where T : MultiplayerCountdown
        {
            if (countdown.IsExclusive)
                await StopAllCountdowns<T>();

            countdown.ID = Interlocked.Increment(ref nextCountdownId);

            CountdownInfo countdownInfo = new CountdownInfo(countdown);

            trackedCountdowns[countdown] = countdownInfo;
            ActiveCountdowns.Add(countdown);

            await eventDispatcher.OnMatchEventAsync(RoomID, new CountdownStartedEvent(countdown));

            countdownInfo.Task = start();

            async Task start()
            {
                // Run the countdown.
                try
                {
                    using (var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(countdownInfo.StopSource.Token, countdownInfo.SkipSource.Token))
                        await Task.Delay(countdownInfo.Duration, cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Clients need to be notified of cancellations in the following code.
                }

                // Notify users that the countdown has finished (or cancelled) and run the continuation.
                // Note: The room must be re-retrieved rather than using our own instance to enforce single-thread access.
                using (var roomUsage = await hub.TryGetRoom(RoomID))
                {
                    try
                    {
                        if (roomUsage?.Item == null)
                            return;

                        if (countdownInfo.StopSource.IsCancellationRequested)
                            return;

                        Debug.Assert(trackedCountdowns.ContainsKey(countdown));
                        Debug.Assert(ActiveCountdowns.Contains(countdown));

                        await StopCountdown(countdown);

                        // The continuation could be run outside of the room lock, however it seems saner to run it within the same lock as the cancellation token usage.
                        // Furthermore, providing a room-id instead of the room becomes cumbersome for usages, so this also provides a nicer API.
                        if (onComplete != null)
                            await onComplete(roomUsage.Item);
                    }
                    finally
                    {
                        countdownInfo.Dispose();
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
            if (!trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return;

            countdownInfo.StopSource.Cancel();

            trackedCountdowns.Remove(countdown);
            ActiveCountdowns.Remove(countdownInfo.Countdown);

            await eventDispatcher.OnMatchEventAsync(RoomID, new CountdownStoppedEvent(countdownInfo.Countdown.ID));
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
            if (countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return Task.CompletedTask;

            countdownInfo.SkipSource.Cancel();
            return countdownInfo.Task;
        }

        /// <summary>
        /// Retrieves the task for the given countdown, if one is running.
        /// </summary>
        /// <param name="countdown">The countdown to retrieve the task of.</param>
        public Task GetCountdownTask(MultiplayerCountdown? countdown)
            => countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo) ? Task.CompletedTask : countdownInfo.Task;

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

        /// <summary>
        /// Retrieves the remaining time for a countdown.
        /// </summary>
        /// <param name="countdown">The countdown.</param>
        /// <returns>The remaining time.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public TimeSpan GetCountdownRemainingTime(MultiplayerCountdown? countdown)
        {
            if (countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return TimeSpan.Zero;

            TimeSpan elapsed = DateTimeOffset.Now - countdownInfo.StartTime;
            return elapsed >= countdownInfo.Duration ? TimeSpan.Zero : countdownInfo.Duration - elapsed;
        }

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
