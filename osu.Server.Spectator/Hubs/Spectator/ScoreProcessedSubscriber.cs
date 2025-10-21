// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using osu.Game.Online.Metadata;
using osu.Game.Online.Spectator;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Metadata;
using StackExchange.Redis;
using StatsdClient;
using Timer = System.Timers.Timer;

namespace osu.Server.Spectator.Hubs.Spectator
{
    public sealed class ScoreProcessedSubscriber : IScoreProcessedSubscriber, IDisposable
    {
        /// <summary>
        /// The maximum amount of time to wait for a <see cref="ScoreProcessed"/> message for a given score in milliseconds.
        /// </summary>
        private const int timeout_interval_ms = 30_000;

        private const string statsd_prefix = "subscribers.score-processed";

        private readonly IDatabaseFactory databaseFactory;
        private readonly ISubscriber? subscriber;

        private readonly ConcurrentDictionary<long, SingleScoreSubscription> singleScoreSubscriptions = new ConcurrentDictionary<long, SingleScoreSubscription>();

        private readonly Dictionary<long, MultiplayerRoomSubscription> multiplayerRoomSubscriptions = new Dictionary<long, MultiplayerRoomSubscription>();

        private readonly Timer timer;
        private readonly ILogger logger;
        private readonly IHubContext<SpectatorHub> spectatorHubContext;
        private readonly IHubContext<MetadataHub> metadataHubContext;

        public ScoreProcessedSubscriber(
            IDatabaseFactory databaseFactory,
            IConnectionMultiplexer redis,
            IHubContext<SpectatorHub> spectatorHubContext,
            IHubContext<MetadataHub> metadataHubContext,
            ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.spectatorHubContext = spectatorHubContext;
            this.metadataHubContext = metadataHubContext;

            timer = new Timer(1000);
            timer.AutoReset = true;
            timer.Elapsed += (_, _) => Task.Run(purgeTimedOutSubscriptions);
            timer.Start();

            subscriber = redis.GetSubscriber();
            subscriber.Subscribe(new RedisChannel("osu-channel:score:processed", RedisChannel.PatternMode.Literal), (_, message) => onMessageReceived(message));

            logger = loggerFactory.CreateLogger(nameof(ScoreProcessedSubscriber));
        }

        private void onMessageReceived(string? message)
        {
            try
            {
                if (string.IsNullOrEmpty(message))
                    return;

                ScoreProcessed? scoreProcessed = JsonConvert.DeserializeObject<ScoreProcessed>(message);

                if (scoreProcessed == null)
                    return;

                if (singleScoreSubscriptions.TryRemove(scoreProcessed.ScoreId, out var subscription))
                {
                    using (subscription)
                        subscription.InvokeAsync().Wait();
                }

                Task.Run(async () => await notifyMultiplayerRoomSubscribers(scoreProcessed));

                DogStatsd.Increment($"{statsd_prefix}.messages.single-score.delivered");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process message");
                DogStatsd.Increment($"{statsd_prefix}.messages.single-score.dropped");
            }
        }

        private async Task notifyMultiplayerRoomSubscribers(ScoreProcessed scoreProcessed)
        {
            try
            {
                using var db = databaseFactory.GetInstance();

                (long roomID, long playlistItemID)? multiplayerLookup = await db.GetMultiplayerRoomIdForScoreAsync(scoreProcessed.ScoreId);

                if (multiplayerLookup == null)
                    return;

                // do one early check to attempt to ensure the database queries we are about to do are not for naught.
                lock (multiplayerRoomSubscriptions)
                {
                    if (!multiplayerRoomSubscriptions.TryGetValue(multiplayerLookup.Value.roomID, out _))
                        return;
                }

                var score = await db.GetScoreAsync(scoreProcessed.ScoreId);
                Debug.Assert(score != null);

                if (!score.passed)
                    return;

                int? newRank = null;
                var userBest = await db.GetUserBestScoreAsync(multiplayerLookup.Value.playlistItemID, (int)score.user_id);

                if (userBest?.score_id == score.id)
                    newRank = await db.GetUserRankInRoomAsync(multiplayerLookup.Value.roomID, (int)score.user_id);

                lock (multiplayerRoomSubscriptions)
                {
                    // do another check just in case something has shifted under us while we were fetching all of the data.
                    if (!multiplayerRoomSubscriptions.TryGetValue(multiplayerLookup.Value.roomID, out var roomSubscription))
                        return;

                    roomSubscription.InvokeAsync(new MultiplayerRoomScoreSetEvent
                    {
                        RoomID = multiplayerLookup.Value.roomID,
                        PlaylistItemID = multiplayerLookup.Value.playlistItemID,
                        ScoreID = (long)score.id,
                        UserID = (int)score.user_id,
                        TotalScore = score.total_score,
                        NewRank = newRank,
                    });
                }

                DogStatsd.Increment($"{statsd_prefix}.messages.room.delivered");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error when attempting to deliver room subscription update");
                DogStatsd.Increment($"{statsd_prefix}.messages.room.dropped");
            }
        }

        public async Task RegisterForSingleScoreAsync(string receiverConnectionId, int userId, long scoreToken)
        {
            try
            {
                using var db = databaseFactory.GetInstance();

                SoloScore? score = await db.GetScoreFromTokenAsync(scoreToken);

                if (score == null)
                {
                    DogStatsd.Increment($"{statsd_prefix}.subscriptions.single-score.dropped");
                    return;
                }

                var subscription = new SingleScoreSubscription(receiverConnectionId, userId, (long)score.id, spectatorHubContext);

                // because the score submission flow happens concurrently with the spectator play finished flow,
                // it is theoretically possible for the score processing to complete before the spectator hub had a chance to register for notifications.
                // to cover off this possibility, check the database directly once.
                if (await db.IsScoreProcessedAsync((long)score.id))
                {
                    using (subscription)
                        await subscription.InvokeAsync();
                    DogStatsd.Increment($"{statsd_prefix}.messages.single-score.delivered-immediately");
                    return;
                }

                singleScoreSubscriptions.TryAdd((long)score.id, subscription);
                DogStatsd.Gauge($"{statsd_prefix}.subscriptions.single-score.total", singleScoreSubscriptions.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register connection {receiverConnectionId} for info about score {userId}:{scoreToken}",
                    receiverConnectionId,
                    userId,
                    scoreToken);
                DogStatsd.Increment($"{statsd_prefix}.subscriptions.single-score.failed");
            }
        }

        public async Task RegisterForMultiplayerRoomAsync(int userId, long roomId)
        {
            using var db = databaseFactory.GetInstance();
            var room = await db.GetRoomAsync(roomId);

            if (room == null)
                return;

            if (room.type != database_match_type.playlists)
            {
                logger.LogError("User {userId} attempted to subscribe for notifications for non-playlists multiplayer room {roomId}. This is currently unsupported.", userId, roomId);
                return;
            }

            lock (multiplayerRoomSubscriptions)
            {
                if (!multiplayerRoomSubscriptions.TryGetValue(roomId, out var existing))
                {
                    multiplayerRoomSubscriptions[roomId] = existing = new MultiplayerRoomSubscription(roomId, metadataHubContext);
                    DogStatsd.Gauge($"{statsd_prefix}.subscriptions.room.total", multiplayerRoomSubscriptions.Count);
                }

                existing.AddUser(userId);
            }
        }

        public Task UnregisterFromMultiplayerRoomAsync(int userId, long roomId)
        {
            lock (multiplayerRoomSubscriptions)
            {
                if (!multiplayerRoomSubscriptions.TryGetValue(roomId, out var subscription))
                    return Task.CompletedTask;

                subscription.RemoveUser(userId);

                if (subscription.UserIds.Count == 0)
                {
                    multiplayerRoomSubscriptions.Remove(roomId);
                    DogStatsd.Gauge($"{statsd_prefix}.subscriptions.room.total", multiplayerRoomSubscriptions.Count);
                }
            }

            return Task.CompletedTask;
        }

        public Task UnregisterFromAllMultiplayerRoomsAsync(int userId)
        {
            lock (multiplayerRoomSubscriptions)
            {
                foreach (var subscription in multiplayerRoomSubscriptions.Values)
                    subscription.RemoveUser(userId);

                long[] emptySubscriptions = multiplayerRoomSubscriptions.Where(kv => kv.Value.UserIds.Count == 0)
                                                                        .Select(kv => kv.Key)
                                                                        .ToArray();

                foreach (long key in emptySubscriptions)
                    multiplayerRoomSubscriptions.Remove(key);

                DogStatsd.Gauge($"{statsd_prefix}.subscriptions.room.total", multiplayerRoomSubscriptions.Count);
            }

            return Task.CompletedTask;
        }

        private void purgeTimedOutSubscriptions()
        {
            long[] scoreIds = singleScoreSubscriptions.Keys.ToArray();
            int purgedCount = 0;

            foreach (long scoreId in scoreIds)
            {
                if (singleScoreSubscriptions.TryGetValue(scoreId, out var subscription) && subscription.TimedOut)
                {
                    subscription.Dispose();

                    if (singleScoreSubscriptions.TryRemove(scoreId, out _))
                        purgedCount += 1;
                }
            }

            if (purgedCount > 0)
            {
                DogStatsd.Gauge($"{statsd_prefix}.subscriptions.single-score.total", singleScoreSubscriptions.Count);
                DogStatsd.Increment($"{statsd_prefix}.subscriptions.single-score.timed-out", purgedCount);
            }

            if (!disposed)
                timer.Start();
        }

        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            subscriber?.UnsubscribeAll();
        }

        private record ScoreProcessed(long ScoreId);

        private class SingleScoreSubscription : IDisposable
        {
            private readonly string receiverConnectionId;
            private readonly int userId;
            private readonly long scoreId;
            private readonly IHubContext<SpectatorHub> spectatorHubContext;

            private readonly CancellationTokenSource cancellationTokenSource;
            public bool TimedOut => cancellationTokenSource.IsCancellationRequested;

            public SingleScoreSubscription(string receiverConnectionId, int userId, long scoreId, IHubContext<SpectatorHub> spectatorHubContext)
            {
                this.receiverConnectionId = receiverConnectionId;
                this.userId = userId;
                this.scoreId = scoreId;
                this.spectatorHubContext = spectatorHubContext;

                cancellationTokenSource = new CancellationTokenSource(timeout_interval_ms);
            }

            public Task InvokeAsync()
                => spectatorHubContext.Clients.Client(receiverConnectionId).SendAsync(nameof(ISpectatorClient.UserScoreProcessed), userId, scoreId);

            public void Dispose() => cancellationTokenSource.Dispose();
        }

        private class MultiplayerRoomSubscription
        {
            public IReadOnlySet<int> UserIds => userIds;

            private readonly HashSet<int> userIds = new HashSet<int>();
            private readonly long roomId;
            private readonly IHubContext<MetadataHub> metadataHubContext;

            public MultiplayerRoomSubscription(long roomId, IHubContext<MetadataHub> metadataHubContext)
            {
                this.roomId = roomId;
                this.metadataHubContext = metadataHubContext;
            }

            public void AddUser(int userId) => userIds.Add(userId);
            public void RemoveUser(int userId) => userIds.Remove(userId);

            public Task InvokeAsync(MultiplayerRoomScoreSetEvent roomScoreSetEvent)
                => metadataHubContext.Clients.Group(MetadataHub.MultiplayerRoomWatchersGroup(roomId)).SendAsync(nameof(IMetadataClient.MultiplayerRoomScoreSet), roomScoreSetEvent);
        }
    }
}
