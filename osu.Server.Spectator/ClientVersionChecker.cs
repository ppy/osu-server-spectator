// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Metadata;

namespace osu.Server.Spectator
{
    public class ClientVersionChecker : IHubFilter
    {
        private readonly EntityStore<MetadataClientState> metadataStore;
        private readonly IDatabaseFactory databaseFactory;
        private readonly IMemoryCache memoryCache;

        public ClientVersionChecker(
            EntityStore<MetadataClientState> metadataStore,
            IDatabaseFactory databaseFactory,
            IMemoryCache memoryCache)
        {
            this.metadataStore = metadataStore;
            this.databaseFactory = databaseFactory;
            this.memoryCache = memoryCache;
        }

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (!await isValidVersionAsync(invocationContext.Context))
                throw new InvalidStateException("Realtime online functionality is not supported on this version of the game. Please upgrade to the latest version.");

            return await next(invocationContext);
        }

        private async Task<bool> isValidVersionAsync(HubCallerContext callerContext)
        {
            if (!AppSettings.ClientCheckVersion)
                return true;

            string? hash;
            using (var item = await metadataStore.GetForUse(callerContext.GetUserId()))
                hash = item.Item?.VersionHash;

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

        private static string cacheKeyForBuild(string buildHash) => $"{nameof(osu_build)}#{buildHash}";
    }
}
