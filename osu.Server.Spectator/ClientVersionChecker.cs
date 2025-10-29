// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

            string? hash = memoryCache.Get<string?>(cacheKeyForClientHash(callerContext.ConnectionId));

            if (string.IsNullOrEmpty(hash))
                return false;

            var build = await memoryCache.GetOrCreateAsync(cacheKeyForBuild(hash), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

                using (var db = databaseFactory.GetInstance())
                    return await db.GetBuildByHashAsync(hash);
            });

            return build?.allow_bancho == true;
        }

        private static string cacheKeyForClientHash(string connectionId) => $"{HubClientConnector.VERSION_HASH_HEADER}#{connectionId}";
        private static string cacheKeyForBuild(string buildHash) => $"{nameof(osu_build)}#{buildHash}";
    }
}
