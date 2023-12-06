// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.Metadata;
using osu.Game.Users;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class MetadataHub : StatefulUserHub<IMetadataClient, MetadataClientState>, IMetadataServer
    {
        private readonly IDatabaseFactory databaseFactory;

        private const string online_presence_watchers_group = "metadata:online-presence-watchers";

        public MetadataHub(
            IDistributedCache cache,
            EntityStore<MetadataClientState> userStates,
            IDatabaseFactory databaseFactory)
            : base(cache, userStates)
        {
            this.databaseFactory = databaseFactory;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            using (var usage = await GetOrCreateLocalUserState())
            {
                usage.Item = new MetadataClientState(Context.ConnectionId, Context.GetUserId());
                await broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence());
            }
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

            await Groups.AddToGroupAsync(Context.ConnectionId, online_presence_watchers_group);
        }

        public Task EndWatchingUserPresence()
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, online_presence_watchers_group);

        public async Task UpdateActivity(UserActivity? activity)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(usage.Item != null);
                usage.Item.UserActivity = activity;

                await broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence());
            }
        }

        public async Task UpdateStatus(UserStatus? status)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(usage.Item != null);
                usage.Item.UserStatus = status;

                await broadcastUserPresenceUpdate(usage.Item.UserId, usage.Item.ToUserPresence());
            }
        }

        protected override async Task CleanUpState(MetadataClientState state)
        {
            await base.CleanUpState(state);
            await broadcastUserPresenceUpdate(state.UserId, null);
        }

        private Task broadcastUserPresenceUpdate(int userId, UserPresence? userPresence)
        {
            if (userPresence?.Status == UserStatus.Offline)
                userPresence = null;

            return Clients.Group(online_presence_watchers_group).UserPresenceUpdated(userId, userPresence);
        }
    }
}
