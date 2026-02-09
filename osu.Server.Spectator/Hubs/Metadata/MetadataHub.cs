// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Metadata;
using osu.Game.Users;
using osu.Server.QueueProcessor;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Spectator;
using BeatmapUpdates = osu.Game.Online.Metadata.BeatmapUpdates;

namespace osu.Server.Spectator.Hubs.Metadata
{
    [Authorize(ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME)]
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
                usage.Item = new MetadataClientState(Context.ConnectionId, Context.GetUserId(), Context.GetVersionHash());

                await logLogin(usage);
                await Clients.Caller.DailyChallengeUpdated(dailyChallengeUpdater.Current);

                await refreshFriends(usage.Item);
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
            QueueProcessor.BeatmapUpdates updates = await BeatmapStatusWatcher.GetUpdatedBeatmapSetsAsync(queueId);
            return new BeatmapUpdates(updates.BeatmapSetIDs, updates.LastProcessedQueueID);
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
                    // Of note, we always send status updates to other users.
                    //
                    // This is a single special case where we don't check against `shouldBroadcastPresentToOtherUsers` because
                    // it is required to tell other clients that "we went offline" in the "appears offline" scenario.
                    broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence()),
                    Clients.Caller.UserPresenceUpdated(usage.Item.UserId, usage.Item.ToUserPresence())
                );
            }

            switch (status)
            {
                case UserStatus.Online:
                case UserStatus.DoNotDisturb:
                    using (var db = databaseFactory.GetInstance())
                        await db.ToggleUserPresenceAsync(Context.GetUserId(), visible: true);
                    break;

                case UserStatus.Offline:
                    using (var db = databaseFactory.GetInstance())
                        await db.ToggleUserPresenceAsync(Context.GetUserId(), visible: false);
                    break;
            }
        }

        private static readonly object update_stats_lock = new object();

        public async Task<MultiplayerPlaylistItemStats[]> BeginWatchingMultiplayerRoom(long id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, MultiplayerRoomWatchersGroup(id));
            await scoreProcessedSubscriber.RegisterForMultiplayerRoomAsync(Context.GetUserId(), id);

            using var db = databaseFactory.GetInstance();

            MultiplayerRoomStats stats = (await cache.GetOrCreateAsync<MultiplayerRoomStats>($@"{nameof(MultiplayerRoomStats)}#{id}", e =>
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

        public async Task RefreshFriends()
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(usage.Item != null);
                await refreshFriends(usage.Item);
            }
        }

        private async Task refreshFriends(MetadataClientState state)
        {
            // Remove the caller from any friend tracking groups.
            foreach (int friendId in state.FriendIds)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, FRIEND_PRESENCE_WATCHERS_GROUP(friendId));

            int[] newFriendIds;

            using (var db = databaseFactory.GetInstance())
            {
                newFriendIds = (await db.GetUserFriendsAsync(state.UserId))
                               // Once upon a time users were able to add themselves as friends.
                               // This errors during the state retrieval below, so let's not support it.
                               .Where(u => u != state.UserId)
                               .ToArray();
            }

            // Add the caller to the friend tracking groups.
            foreach (int friendId in newFriendIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, FRIEND_PRESENCE_WATCHERS_GROUP(friendId));

            // Broadcast presence from any online friends to the caller.
            foreach (int friendId in newFriendIds.Except(state.FriendIds))
            {
                using (var friendUsage = await TryGetStateFromUser(friendId))
                {
                    if (friendUsage?.Item != null && shouldBroadcastPresenceToOtherUsers(friendUsage.Item))
                        await Clients.Caller.FriendPresenceUpdated(friendId, friendUsage.Item.ToUserPresence());
                }
            }

            state.FriendIds = newFriendIds;
        }

        protected override async Task CleanUpState(ItemUsage<MetadataClientState> state)
        {
            Debug.Assert(state.Item != null);

            await base.CleanUpState(state);
            if (shouldBroadcastPresenceToOtherUsers(state.Item))
                await broadcastUserPresenceUpdate(state.Item.UserId, null);
            await scoreProcessedSubscriber.UnregisterFromAllMultiplayerRoomsAsync(state.Item.UserId);
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
