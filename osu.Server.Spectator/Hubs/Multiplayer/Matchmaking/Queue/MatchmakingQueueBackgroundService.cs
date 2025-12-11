// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Services;
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
        private static string queue_ban_start_time(int userId) => $"matchmaking-ban-start-time:{userId}";

        private readonly ConcurrentDictionary<int, MatchmakingQueue> poolQueues = new ConcurrentDictionary<int, MatchmakingQueue>();
        private readonly ConcurrentDictionary<uint, MatchmakingBeatmapSelector> poolSelectors = new ConcurrentDictionary<uint, MatchmakingBeatmapSelector>();

        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;
        private readonly IDatabaseFactory databaseFactory;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly IMultiplayerHubContext hubContext;
        private readonly ILogger logger;
        private readonly IMemoryCache memoryCache;
        private readonly MultiplayerEventLogger eventLogger;

        private DateTimeOffset lastLobbyUpdateTime = DateTimeOffset.UnixEpoch;
        private DateTimeOffset lastQueueRefreshTime = DateTimeOffset.UnixEpoch;
        private DateTimeOffset lastPoolRefreshTime = DateTimeOffset.UnixEpoch;

        public MatchmakingQueueBackgroundService(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop, IDatabaseFactory databaseFactory, ILoggerFactory loggerFactory,
                                                 EntityStore<ServerMultiplayerRoom> rooms, IMultiplayerHubContext hubContext, IMemoryCache memoryCache, MultiplayerEventLogger eventLogger)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
            this.databaseFactory = databaseFactory;
            this.rooms = rooms;
            this.hubContext = hubContext;
            this.memoryCache = memoryCache;
            this.eventLogger = eventLogger;

            logger = loggerFactory.CreateLogger(nameof(MatchmakingQueueBackgroundService));
        }

        public bool IsInQueue(MultiplayerClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
            {
                if (queue.IsInQueue(new MatchmakingQueueUser(state.ConnectionId)))
                    return true;
            }

            return false;
        }

        public async Task AddToLobbyAsync(MultiplayerClientState state)
        {
            await hub.Groups.AddToGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task RemoveFromLobbyAsync(MultiplayerClientState state)
        {
            await hub.Groups.RemoveFromGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task AddToQueueAsync(MultiplayerClientState state, int poolId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                matchmaking_pool pool = await db.GetMatchmakingPoolAsync((uint)poolId) ?? throw new InvalidStateException($"Pool not found: {poolId}");

                if (!pool.active)
                    throw new InvalidStateException("The selected matchmaking pool is no longer active.");

                matchmaking_user_stats? stats = await db.GetMatchmakingUserStatsAsync(state.UserId, (uint)poolId);

                if (stats == null)
                {
                    // Estimate initial elo from PP.
                    double pp = await db.GetUserPPAsync(state.UserId, pool.ruleset_id);
                    double eloEstimate = -4000 + 600 * Math.Log(pp + 4000);

                    await db.UpdateMatchmakingUserStatsAsync(stats = new matchmaking_user_stats
                    {
                        user_id = (uint)state.UserId,
                        pool_id = pool.id,
                        EloData =
                        {
                            InitialRating = new EloRating(eloEstimate),
                            NormalFactor = new EloRating(eloEstimate),
                            ApproximatePosterior = new EloRating(eloEstimate)
                        }
                    });
                }

                MatchmakingQueueUser user = new MatchmakingQueueUser(state.ConnectionId)
                {
                    UserId = state.UserId,
                    Rating = stats.EloData.ApproximatePosterior,
                    QueueBanStartTime = memoryCache.Get<DateTimeOffset?>(queue_ban_start_time(state.UserId)) ?? DateTimeOffset.MinValue
                };

                MatchmakingQueue queue = poolQueues.GetOrAdd(poolId, _ => new MatchmakingQueue(pool));
                await processBundle(queue.Add(user));
            }
        }

        public async Task RemoveFromQueueAsync(MultiplayerClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.Remove(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task AcceptInvitationAsync(MultiplayerClientState state)
        {
            // Immediately notify the incoming user of their intent to join the match.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.JoiningMatch());

            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.MarkInvitationAccepted(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task DeclineInvitationAsync(MultiplayerClientState state)
        {
            foreach ((_, MatchmakingQueue queue) in poolQueues)
                await processBundle(queue.MarkInvitationDeclined(new MatchmakingQueueUser(state.ConnectionId)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExecuteOnceAsync();
                await Task.Delay(queue_update_rate, stoppingToken);
            }
        }

        /// <summary>
        /// Executes a single update of the queues.
        /// </summary>
        public async Task ExecuteOnceAsync()
        {
            try
            {
                await updateLobby();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update the matchmaking lobby.");
            }

            try
            {
                await refreshQueues();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh the matchmaking queue.");
            }

            await refreshPools();

            foreach ((_, MatchmakingQueue queue) in poolQueues)
            {
                try
                {
                    await processBundle(queue.Update());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update the matchmaking queue for pool {poolId}.", queue.Pool.id);
                }
            }
        }

        private async Task updateLobby()
        {
            if (DateTimeOffset.Now - lastLobbyUpdateTime < lobby_update_rate)
                return;

            foreach ((_, MatchmakingQueue queue) in poolQueues)
                DogStatsd.Gauge($"{statsd_prefix}.queue.users", queue.Count, tags: [$"queue:{queue.Pool.DisplayName}"]);

            MatchmakingQueueUser[] users = poolQueues.Values.SelectMany(queue => queue.GetAllUsers()).ToArray();
            Random.Shared.Shuffle(users);
            int[] usersSample = users.Take(50).Select(u => u.UserId).ToArray();

            await hub.Clients.Group(lobby_users_group).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), new MatchmakingLobbyStatus
            {
                UsersInQueue = usersSample
            });

            lastLobbyUpdateTime = DateTimeOffset.Now;
        }

        private async Task refreshQueues()
        {
            if (DateTimeOffset.Now - lastQueueRefreshTime < TimeSpan.FromMinutes(1))
                return;

            using (var db = databaseFactory.GetInstance())
            {
                foreach ((_, MatchmakingQueue queue) in poolQueues)
                {
                    matchmaking_pool? newPool = await db.GetMatchmakingPoolAsync(queue.Pool.id);

                    if (newPool?.active != true)
                        await processBundle(queue.Clear());
                    else
                        queue.Refresh(newPool);
                }
            }

            lastQueueRefreshTime = DateTimeOffset.Now;
        }

        private Task refreshPools()
        {
            if (DateTimeOffset.Now - lastPoolRefreshTime < TimeSpan.FromHours(1))
                return Task.CompletedTask;

            poolSelectors.Clear();

            lastPoolRefreshTime = DateTimeOffset.Now;
            return Task.CompletedTask;
        }

        private async Task processBundle(MatchmakingQueueUpdateBundle bundle)
        {
            foreach (var user in bundle.DeclinedUsers)
            {
                // Right now this will just delay the user from being included in matchmaking for a set period.
                // This will be silent to users affected (see `MatchmakingQueue.matchUsers`).
                //
                // TODO: we should probably let the players know that they have been penalised.
                memoryCache.Set(queue_ban_start_time(user.UserId), bundle.Queue.Clock.UtcNow);
            }

            foreach (var user in bundle.RemovedUsers)
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueLeft));

            foreach (var user in bundle.AddedUsers)
            {
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueJoined));
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.Searching());
            }

            foreach (var group in bundle.RecycledGroups)
            {
                DogStatsd.Increment($"{statsd_prefix}.groups.recycled");

                foreach (var user in group.Users)
                    await hub.Groups.RemoveFromGroupAsync(user.Identifier, group.Identifier);
            }

            foreach (var group in bundle.FormedGroups)
            {
                DogStatsd.Increment($"{statsd_prefix}.groups.formed");

                foreach (var user in group.Users)
                    await hub.Groups.AddToGroupAsync(user.Identifier, group.Identifier, CancellationToken.None);

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingRoomInvited));
                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.MatchFound());
            }

            foreach (var group in bundle.CompletedGroups)
            {
                DogStatsd.Increment($"{statsd_prefix}.groups.completed");

                foreach (var user in group.Users)
                    DogStatsd.Timer($"{statsd_prefix}.queue.duration", (DateTimeOffset.Now - user.SearchStartTime).TotalMilliseconds, tags: [$"queue:{bundle.Queue.Pool.DisplayName}"]);

                string password = Guid.NewGuid().ToString();
                long roomId = await sharedInterop.CreateRoomAsync(AppSettings.BanchoBotUserId, new MultiplayerRoom(0)
                {
                    Settings =
                    {
                        MatchType = MatchType.Matchmaking,
                        Password = password
                    },
                    Playlist = await queryPlaylistItems(bundle.Queue.Pool, group.Users.Select(u => u.Rating).ToArray())
                });

                // Initialise the room and users
                using (var roomUsage = await rooms.GetForUse(roomId, true))
                    roomUsage.Item = await InitialiseRoomAsync(roomId, hubContext, databaseFactory, eventLogger, group.Users.Select(u => u.UserId).ToArray(), bundle.Queue.Pool.id);

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMatchmakingClient.MatchmakingRoomReady), roomId, password);

                foreach (var user in group.Users)
                    await hub.Groups.RemoveFromGroupAsync(user.Identifier, group.Identifier);

                await eventLogger.LogMatchmakingRoomCreatedAsync(roomId, new MatchmakingRoomCreatedEventDetail
                {
                    pool_id = (int)bundle.Queue.Pool.id
                });
            }
        }

        /// <summary>
        /// Initialises a matchmaking room with the given eligible user IDs.
        /// </summary>
        /// <param name="roomId">The room identifier.</param>
        /// <param name="hub">The multiplayer hub context.</param>
        /// <param name="dbFactory">The database factory.</param>
        /// <param name="eventLogger">The event logger.</param>
        /// <param name="eligibleUserIds">The users who are allowed to join the room.</param>
        /// <param name="poolId">The pool ID.</param>
        /// <exception cref="InvalidOperationException">If the room is not a matchmaking room in the database.</exception>
        public static async Task<ServerMultiplayerRoom> InitialiseRoomAsync(long roomId, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventLogger eventLogger,
                                                                            int[] eligibleUserIds, uint poolId)
        {
            ServerMultiplayerRoom room = await ServerMultiplayerRoom.InitialiseAsync(roomId, hub, dbFactory, eventLogger);

            if (room.MatchState is not MatchmakingRoomState matchmakingState)
                throw new InvalidOperationException("Failed to initialise the matchmaking room (invalid state).");

            foreach (int user in eligibleUserIds)
                matchmakingState.Users.GetOrAdd(user);

            if (room.Controller is not MatchmakingMatchController matchmakingController)
                throw new InvalidOperationException("Failed to initialise the matchmaking room (invalid controller).");

            matchmakingController.PoolId = poolId;

            return room;
        }

        private async Task<MultiplayerPlaylistItem[]> queryPlaylistItems(matchmaking_pool pool, EloRating[] ratings)
        {
            if (!poolSelectors.TryGetValue(pool.id, out MatchmakingBeatmapSelector? selector))
                poolSelectors[pool.id] = selector = await MatchmakingBeatmapSelector.Initialise(pool, databaseFactory);

            matchmaking_pool_beatmap[] items = selector.GetAppropriateBeatmaps(ratings);

            return items.Select(b => new MultiplayerPlaylistItem
            {
                BeatmapID = b.beatmap_id,
                BeatmapChecksum = b.checksum!,
                RulesetID = pool.ruleset_id,
                StarRating = b.difficultyrating,
                RequiredMods = JsonConvert.DeserializeObject<APIMod[]>(b.mods ?? string.Empty) ?? [],
            }).ToArray();
        }
    }
}
