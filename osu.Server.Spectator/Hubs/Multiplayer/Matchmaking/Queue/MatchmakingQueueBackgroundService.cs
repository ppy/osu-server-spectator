// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Services;
using Sentry;
using StatsdClient;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingQueueBackgroundService : BackgroundService, IMatchmakingQueueBackgroundService
    {
        /// <summary>
        /// The rate at which the matchmaking queue is updated.
        /// </summary>
        private static readonly TimeSpan queue_update_rate = AppSettings.MatchmakingQueueUpdateRate;

        /// <summary>
        /// The rate at which users are sent lobby status updates.
        /// </summary>
        private static readonly TimeSpan lobby_update_rate = AppSettings.MatchmakingLobbyUpdateRate;

        private const string lobby_users_group = "matchmaking-lobby-users";
        private const string statsd_prefix = "matchmaking";

        private readonly ConcurrentDictionary<int, MatchmakingQueue> poolQueues = new ConcurrentDictionary<int, MatchmakingQueue>();

        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger logger;

        private DateTimeOffset lastLobbyUpdateTime = DateTimeOffset.UnixEpoch;

        public MatchmakingQueueBackgroundService(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop, IDatabaseFactory databaseFactory, ILoggerFactory loggerFactory)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
            this.databaseFactory = databaseFactory;

            logger = loggerFactory.CreateLogger(nameof(MatchmakingQueueBackgroundService));
        }

        public bool IsInQueue(MatchmakingClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
            {
                if (queue.IsInQueue(new MatchmakingQueueUser(state.ConnectionId)))
                    return true;
            }

            return false;
        }

        public async Task AddToLobbyAsync(MatchmakingClientState state)
        {
            await hub.Groups.AddToGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task RemoveFromLobbyAsync(MatchmakingClientState state)
        {
            await hub.Groups.RemoveFromGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task AddToQueueAsync(MatchmakingClientState state, int poolId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                matchmaking_pool pool = await db.GetMatchmakingPoolAsync(poolId) ?? throw new InvalidStateException($"Pool not found: {poolId}");
                matchmaking_user_stats? stats = await db.GetMatchmakingUserStatsAsync(state.UserId, pool.ruleset_id);

                if (stats == null)
                {
                    const int min_elo = 1400;
                    const int max_elo = 1600;

                    // Estimate Elo from PP.
                    double pp = await db.GetUserPPAsync(state.UserId, pool.ruleset_id);
                    double eloEstimate = min_elo + pp / 25000 * (max_elo - min_elo);

                    await db.UpdateMatchmakingUserStatsAsync(stats = new matchmaking_user_stats
                    {
                        user_id = (uint)state.UserId,
                        ruleset_id = (ushort)pool.ruleset_id,
                        EloData =
                        {
                            NormalFactor = { Mu = eloEstimate },
                            ApproximatePosterior = { Mu = eloEstimate }
                        }
                    });
                }

                MatchmakingQueueUser user = new MatchmakingQueueUser(state.ConnectionId)
                {
                    UserId = state.UserId,
                    Rating = stats.EloData.ApproximatePosterior
                };

                MatchmakingQueue queue = poolQueues.GetOrAdd(poolId, _ => new MatchmakingQueue(pool));
                await processBundle(queue.Add(user));
            }
        }

        public async Task RemoveFromQueueAsync(MatchmakingClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.Remove(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task AcceptInvitationAsync(MatchmakingClientState state)
        {
            // Immediately notify the incoming user of their intent to join the match.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.JoiningMatch());

            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.MarkInvitationAccepted(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task DeclineInvitationAsync(MatchmakingClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.MarkInvitationDeclined(new MatchmakingQueueUser(state.ConnectionId)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await updateLobby();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update the matchmaking lobby.");
                    SentrySdk.CaptureException(ex);
                }

                foreach ((_, MatchmakingQueue queue) in poolQueues)
                {
                    try
                    {
                        await processBundle(queue.Update());
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to update the matchmaking queue for pool {poolId}.", queue.Pool.id);
                        SentrySdk.CaptureException(ex);
                    }
                }

                await Task.Delay(queue_update_rate, stoppingToken);
            }
        }

        private async Task updateLobby()
        {
            if (DateTimeOffset.Now - lastLobbyUpdateTime < lobby_update_rate)
                return;

            MatchmakingQueueUser[] users = poolQueues.Values.SelectMany(queue => queue.GetAllUsers()).ToArray();
            DogStatsd.Counter($"{statsd_prefix}.users_queued", users.Length);

            Random.Shared.Shuffle(users);
            int[] usersSample = users.Take(50).Select(u => u.UserId).ToArray();

            await hub.Clients.Group(lobby_users_group).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), new MatchmakingLobbyStatus
            {
                UsersInQueue = usersSample
            });

            lastLobbyUpdateTime = DateTimeOffset.Now;
        }

        private async Task processBundle(MatchmakingQueueUpdateBundle bundle)
        {
            foreach (var user in bundle.RemovedUsers)
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueLeft));

            foreach (var user in bundle.AddedUsers)
            {
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueJoined));
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.Searching());
            }

            foreach (var group in bundle.FormedGroups)
            {
                DogStatsd.Increment($"{statsd_prefix}.groups_formed");

                foreach (var user in group.Users)
                    await hub.Groups.AddToGroupAsync(user.Identifier, group.Identifier, CancellationToken.None);

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingRoomInvited));
                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.MatchFound());
            }

            foreach (var group in bundle.CompletedGroups)
            {
                DogStatsd.Increment($"{statsd_prefix}.groups_completed");
                foreach (var user in group.Users)
                    DogStatsd.Timer($"{statsd_prefix}.queue_wait_time", (user.SearchStartTime - DateTimeOffset.Now).TotalMilliseconds);

                string password = Guid.NewGuid().ToString();
                long roomId = await sharedInterop.CreateRoomAsync(AppSettings.BanchoBotUserId, new MultiplayerRoom(0)
                {
                    Settings =
                    {
                        MatchType = MatchType.Matchmaking,
                        Password = password
                    },
                    Playlist = await queryPlaylistItems(bundle.Queue.Pool)
                });

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingRoomReady), roomId, password);

                foreach (var user in group.Users)
                    await hub.Groups.RemoveFromGroupAsync(user.Identifier, group.Identifier);
            }
        }

        private async Task<MultiplayerPlaylistItem[]> queryPlaylistItems(matchmaking_pool pool)
        {
            using (var db = databaseFactory.GetInstance())
            {
                matchmaking_pool_beatmap[] beatmaps = await db.GetMatchmakingPoolBeatmapsAsync(pool.id);
                return beatmaps.Select(b => new MultiplayerPlaylistItem
                {
                    BeatmapID = b.beatmap_id,
                    BeatmapChecksum = b.checksum!,
                    RulesetID = pool.ruleset_id,
                    StarRating = b.difficultyrating,
                }).ToArray();
            }
        }
    }
}
