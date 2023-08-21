// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Online.Spectator;
using osu.Server.Spectator.Database;
using StackExchange.Redis;
using StatsdClient;
using Timer = System.Timers.Timer;

namespace osu.Server.Spectator.Hubs.Spectator;

public sealed class ScoreProcessedSubscriber : IScoreProcessedSubscriber, IDisposable
{
    /// <summary>
    /// The maximum amount of time to wait for a <see cref="ScoreProcessed"/> message for a given score in milliseconds.
    /// </summary>
    private const int timeout_interval_ms = 30_000;

    private const string statsd_prefix = "subscribers.score-processed";

    private readonly IDatabaseFactory databaseFactory;
    private readonly ISubscriber? subscriber;

    private readonly ConcurrentDictionary<long, ScoreProcessedSubscription> subscriptions = new ConcurrentDictionary<long, ScoreProcessedSubscription>();
    private readonly Timer timer;
    private readonly Logger logger;
    private readonly IHubContext<SpectatorHub> spectatorHubContext;

    public ScoreProcessedSubscriber(
        IDatabaseFactory databaseFactory,
        IConnectionMultiplexer redis,
        IHubContext<SpectatorHub> spectatorHubContext)
    {
        this.databaseFactory = databaseFactory;
        this.spectatorHubContext = spectatorHubContext;

        timer = new Timer(1000);
        timer.AutoReset = true;
        timer.Elapsed += (_, _) => Task.Run(purgeTimedOutSubscriptions);
        timer.Start();

        subscriber = redis.GetSubscriber();
        subscriber.Subscribe("osu-channel:score:processed", (_, message) => onMessageReceived(message));

        logger = Logger.GetLogger(nameof(ScoreProcessedSubscriber));
    }

    private void onMessageReceived(string message)
    {
        try
        {
            var scoreProcessed = JsonConvert.DeserializeObject<ScoreProcessed>(message);

            if (scoreProcessed == null)
                return;

            if (subscriptions.TryRemove(scoreProcessed.ScoreId, out var subscription))
            {
                using (subscription)
                    subscription.InvokeAsync().Wait();
            }

            DogStatsd.Increment($"{statsd_prefix}.messages.delivered");
        }
        catch (Exception ex)
        {
            logger.Add($"Failed to process message {message}", LogLevel.Error, ex);
            DogStatsd.Increment($"{statsd_prefix}.messages.dropped");
        }
    }

    public async Task RegisterForNotificationAsync(string receiverConnectionId, int userId, long scoreToken)
    {
        try
        {
            using var db = databaseFactory.GetInstance();

            long? scoreId = await db.GetScoreIdFromToken(scoreToken);

            if (scoreId == null)
            {
                DogStatsd.Increment($"{statsd_prefix}.subscriptions.dropped");
                return;
            }

            var subscription = new ScoreProcessedSubscription(receiverConnectionId, userId, scoreId.Value, spectatorHubContext);

            // because the score submission flow happens concurrently with the spectator play finished flow,
            // it is theoretically possible for the score processing to complete before the spectator hub had a chance to register for notifications.
            // to cover off this possibility, check the database directly once.
            if (await db.IsScoreProcessedAsync(scoreId.Value))
            {
                using (subscription)
                    await subscription.InvokeAsync();
                DogStatsd.Increment($"{statsd_prefix}.messages.delivered-immediately");
                return;
            }

            subscriptions.TryAdd(scoreId.Value, subscription);
            DogStatsd.Gauge($"{statsd_prefix}.subscriptions.total", subscriptions.Count);
        }
        catch (Exception ex)
        {
            logger.Add($"Failed to register connection {receiverConnectionId} for info about score {userId}:{scoreToken}", LogLevel.Error, ex);
            DogStatsd.Increment($"{statsd_prefix}.subscriptions.failed");
        }
    }

    private void purgeTimedOutSubscriptions()
    {
        var scoreIds = subscriptions.Keys.ToArray();
        int purgedCount = 0;

        foreach (var scoreId in scoreIds)
        {
            if (subscriptions.TryGetValue(scoreId, out var subscription) && subscription.TimedOut)
            {
                subscription.Dispose();

                if (subscriptions.TryRemove(scoreId, out _))
                    purgedCount += 1;
            }
        }

        if (purgedCount > 0)
        {
            DogStatsd.Gauge($"{statsd_prefix}.subscriptions.total", subscriptions.Count);
            DogStatsd.Increment($"{statsd_prefix}.subscriptions.timed-out", purgedCount);
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

    private class ScoreProcessedSubscription : IDisposable
    {
        private readonly string receiverConnectionId;
        private readonly int userId;
        private readonly long scoreId;
        private readonly IHubContext<SpectatorHub> spectatorHubContext;

        private readonly CancellationTokenSource cancellationTokenSource;
        public bool TimedOut => cancellationTokenSource.IsCancellationRequested;

        public ScoreProcessedSubscription(string receiverConnectionId, int userId, long scoreId, IHubContext<SpectatorHub> spectatorHubContext)
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
}
