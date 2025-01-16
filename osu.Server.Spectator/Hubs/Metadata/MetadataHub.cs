// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;
using osu.Game.Online;
using osu.Game.Online.Metadata;
using osu.Game.Users;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Spectator;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class MetadataHub : StatefulUserHub<IMetadataClient, MetadataClientState>, IMetadataServer
    {
        private readonly IMemoryCache cache;
        private readonly IDatabaseFactory databaseFactory;
        private readonly IDailyChallengeUpdater dailyChallengeUpdater;
        private readonly IScoreProcessedSubscriber scoreProcessedSubscriber;

        internal const string ONLINE_PRESENCE_WATCHERS_GROUP = "metadata:online-presence-watchers";
        internal static string FRIEND_PRESENCE_WATCHERS_GROUP(int userId) => $"metadata:online-presence-watchers:{userId}";

        internal static string MultiplayerRoomWatchersGroup(long roomId) => $"metadata:multiplayer-room-watchers:{roomId}";

        public MetadataHub(
            ILoggerFactory loggerFactory,
            IMemoryCache cache,
            EntityStore<MetadataClientState> userStates,
            IDatabaseFactory databaseFactory,
            IDailyChallengeUpdater dailyChallengeUpdater,
            IScoreProcessedSubscriber scoreProcessedSubscriber)
            : base(loggerFactory, userStates)
        {
            this.cache = cache;
            this.databaseFactory = databaseFactory;
            this.dailyChallengeUpdater = dailyChallengeUpdater;
            this.scoreProcessedSubscriber = scoreProcessedSubscriber;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            using (var usage = await GetOrCreateLocalUserState())
            {
                string? versionHash = null;

                if (Context.GetHttpContext()?.Request.Headers.TryGetValue(HubClientConnector.VERSION_HASH_HEADER, out StringValues headerValue) == true)
                {
                    versionHash = headerValue;

                    // The token is 82 chars long, and the clientHash is the first 32 of those.
                    // See: https://github.com/ppy/osu-web/blob/7be19a0fe0c9fa2f686e4bb686dbc8e9bf7bcf84/app/Libraries/ClientCheck.php#L92
                    if (versionHash?.Length >= 82)
                        versionHash = versionHash.Substring(versionHash.Length - 82, 32);
                }

                usage.Item = new MetadataClientState(Context.ConnectionId, Context.GetUserId(), versionHash);

                await logLogin(usage);
                await Clients.Caller.DailyChallengeUpdated(dailyChallengeUpdater.Current);

                using (var db = databaseFactory.GetInstance())
                {
                    foreach (int friendId in await db.GetUserFriendsAsync(usage.Item.UserId))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, FRIEND_PRESENCE_WATCHERS_GROUP(friendId));

                        // Check if the friend is online, and if they are, broadcast to the connected user.
                        using (var friendUsage = await TryGetStateFromUser(friendId))
                        {
                            if (friendUsage?.Item != null && shouldBroadcastPresenceToOtherUsers(friendUsage.Item))
                                await Clients.Caller.FriendPresenceUpdated(friendId, friendUsage.Item.ToUserPresence());
                        }
                    }
                }
            }
        }

        private async Task logLogin(ItemUsage<MetadataClientState> usage)
        {
            string? userIp = Context.GetHttpContext()?.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedForIp) == true
                // header may contain multiple IPs by spec, first is usually what we care for.
                ? forwardedForIp.ToString().Split(',').First()
                // fallback to getting the raw IP.
                : Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

            using (var db = databaseFactory.GetInstance())
                await db.AddLoginForUserAsync(usage.Item!.UserId, userIp);
        }

        public async Task<BeatmapUpdates> GetChangesSince(int queueId)
        {
            using (var db = databaseFactory.GetInstance())
                return await db.GetUpdatedBeatmapSets(queueId);
        }

        public async Task BeginWatchingUserPresence()
        {
            foreach (var userState in GetAllStates())
            {
                if (userState.Value.UserStatus != UserStatus.Offline)
                    await Clients.Caller.UserPresenceUpdated(userState.Value.UserId, userState.Value.ToUserPresence());
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ONLINE_PRESENCE_WATCHERS_GROUP);
        }

        public Task EndWatchingUserPresence()
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, ONLINE_PRESENCE_WATCHERS_GROUP);

        public async Task UpdateActivity(UserActivity? activity)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(usage.Item != null);

                if (usage.Item.UserActivity == null && activity == null)
                    return;

                usage.Item.UserActivity = activity;

                await Task.WhenAll
                (
                    shouldBroadcastPresenceToOtherUsers(usage.Item)
                        ? broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence())
                        : Task.CompletedTask,
                    Clients.Caller.UserPresenceUpdated(usage.Item.UserId, usage.Item.ToUserPresence())
                );
            }
        }

        public async Task UpdateStatus(UserStatus? status)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(usage.Item != null);

                if (usage.Item.UserStatus == status)
                    return;

                usage.Item.UserStatus = status;

                await Task.WhenAll
                (
                    // special case of users that already broadcast that they are online switching to "appear offline".
                    broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence()),
                    Clients.Caller.UserPresenceUpdated(usage.Item.UserId, usage.Item.ToUserPresence())
                );
            }
        }

        private static readonly object update_stats_lock = new object();

        public async Task<MultiplayerPlaylistItemStats[]> BeginWatchingMultiplayerRoom(long id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, MultiplayerRoomWatchersGroup(id));
            await scoreProcessedSubscriber.RegisterForMultiplayerRoomAsync(Context.GetUserId(), id);

            using var db = databaseFactory.GetInstance();

            MultiplayerRoomStats stats = (await cache.GetOrCreateAsync<MultiplayerRoomStats>(id.ToString(), e =>
            {
                e.SlidingExpiration = TimeSpan.FromHours(24);
                return Task.FromResult(new MultiplayerRoomStats { RoomID = id });
            }))!;

            await updateMultiplayerRoomStatsAsync(db, stats);

            // Outside of locking so may be mid-update, but that's fine we don't need perfectly accurate for client-side.
            return stats.PlaylistItemStats.Values.ToArray();
        }

        private async Task updateMultiplayerRoomStatsAsync(IDatabaseAccess db, MultiplayerRoomStats stats)
        {
            long[] playlistItemIds = (await db.GetAllPlaylistItemsAsync(stats.RoomID)).Select(item => item.id).ToArray();

            for (int i = 0; i < playlistItemIds.Length; ++i)
            {
                long itemId = playlistItemIds[i];

                if (!stats.PlaylistItemStats.TryGetValue(itemId, out var itemStats))
                    stats.PlaylistItemStats[itemId] = itemStats = new MultiplayerPlaylistItemStats { PlaylistItemID = itemId, };

                ulong lastProcessed = itemStats.LastProcessedScoreID;

                SoloScore[] scores = (await db.GetPassingScoresForPlaylistItem(itemId, itemStats.LastProcessedScoreID)).ToArray();

                if (scores.Length == 0)
                    return;

                // Lock globally for simplicity.
                // If it ever becomes an issue we can move to per-item locking or something more complex.
                lock (update_stats_lock)
                {
                    // check whether last id has changed since database query completed. if it did, this means another run would have updated the stats.
                    // for simplicity, just skip the update and wait for the next.
                    if (lastProcessed == itemStats.LastProcessedScoreID)
                    {
                        Dictionary<int, long> totals = scores
                                                       .Select(s => s.total_score)
                                                       .GroupBy(score => (int)Math.Clamp(Math.Floor((float)score / 100000), 0, MultiplayerPlaylistItemStats.TOTAL_SCORE_DISTRIBUTION_BINS - 1))
                                                       .OrderBy(grp => grp.Key)
                                                       .ToDictionary(grp => grp.Key, grp => grp.LongCount());

                        itemStats.CumulativeScore += scores.Sum(s => s.total_score);
                        for (int j = 0; j < MultiplayerPlaylistItemStats.TOTAL_SCORE_DISTRIBUTION_BINS; j++)
                            itemStats.TotalScoreDistribution[j] += totals.GetValueOrDefault(j);
                        itemStats.LastProcessedScoreID = scores.Max(s => s.id);
                    }
                }
            }
        }

        public async Task EndWatchingMultiplayerRoom(long id)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, MultiplayerRoomWatchersGroup(id));
            await scoreProcessedSubscriber.UnregisterFromMultiplayerRoomAsync(Context.GetUserId(), id);
        }

        protected override async Task CleanUpState(MetadataClientState state)
        {
            await base.CleanUpState(state);
            if (shouldBroadcastPresenceToOtherUsers(state))
                await broadcastUserPresenceUpdate(state.UserId, null);
            await scoreProcessedSubscriber.UnregisterFromAllMultiplayerRoomsAsync(state.UserId);
        }

        private Task broadcastUserPresenceUpdate(int userId, UserPresence? userPresence)
        {
            // we never want appearing offline users to have their status broadcast to other clients.
            Debug.Assert(userPresence?.Status != UserStatus.Offline);

            return Task.WhenAll
            (
                Clients.Group(ONLINE_PRESENCE_WATCHERS_GROUP).UserPresenceUpdated(userId, userPresence),
                Clients.Group(FRIEND_PRESENCE_WATCHERS_GROUP(userId)).FriendPresenceUpdated(userId, userPresence)
            );
        }

        private bool shouldBroadcastPresenceToOtherUsers(MetadataClientState state)
        {
            if (state.UserStatus == null)
                return false;

            switch (state.UserStatus.Value)
            {
                case UserStatus.Offline:
                    return false;

                case UserStatus.DoNotDisturb:
                case UserStatus.Online:
                    return true;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
