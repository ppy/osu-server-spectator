// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator
{
    public class ClientVersionChecker : IHubFilter
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IMemoryCache memoryCache;

        public ClientVersionChecker(
            IDatabaseFactory databaseFactory,
            IMemoryCache memoryCache)
        {
            this.databaseFactory = databaseFactory;
            this.memoryCache = memoryCache;
        }

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            memoryCache.GetOrCreate(cacheKeyForClientHash(context.Context.ConnectionId), _ => context.Context.GetVersionHash());
            await next(context);
        }

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (!await isValidVersionAsync(invocationContext.Context))
                throw new InvalidStateException("Realtime online functionality is not supported on this version of the game. Please upgrade to the latest version.");

            return await next(invocationContext);
        }

        public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
        {
            memoryCache.Remove(cacheKeyForClientHash(context.Context.ConnectionId));
            await next(context, exception);
        }

        private async Task<bool> isValidVersionAsync(HubCallerContext callerContext)
        {
            if (!AppSettings.ClientCheckVersion)
                return true;

            HashSet<int> exemptUsers = await memoryCache.GetOrCreateAsync<HashSet<int>>(cache_key_for_exempt_users, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);

                using (var db = databaseFactory.GetInstance())
                    return new HashSet<int>(await db.GetUsersInGroupsAsync(AppSettings.ClientCheckVersionExemptGroups));
            }) ?? [];

            if (exemptUsers.Contains(callerContext.GetUserId()))
                return true;

            string? hash = memoryCache.Get<string?>(cacheKeyForClientHash(callerContext.ConnectionId));

            if (string.IsNullOrEmpty(hash))
                return false;

            var build = await memoryCache.GetOrCreateAsync(cacheKeyForBuild(hash), async entry =>
            {
                osu_build? build;

                using (var db = databaseFactory.GetInstance())
                    build = await db.GetBuildByHashAsync(hash);

                if (build == null)
                    entry.Dispose();
                else
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

                return build;
            });

            return build?.allow_bancho == true;
        }

        private static string cacheKeyForClientHash(string connectionId) => $"{HubClientConnector.VERSION_HASH_HEADER}#{connectionId}";
        private static string cacheKeyForBuild(string buildHash) => $"{nameof(osu_build)}#{buildHash}";
        private const string cache_key_for_exempt_users = $"{nameof(ClientVersionChecker)}#exempt_users";
    }
}
