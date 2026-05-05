// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Requests;
using osu.Game.Online.Matchmaking.Responses;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : IMatchmakingServer
    {
        // Provided for backwards compatibility. Can be removed 20260727.
        public Task<MatchmakingPool[]> GetMatchmakingPools()
            => GetMatchmakingPoolsOfType(MatchmakingPoolType.QuickPlay);

        // Provided for backwards compatibility. Can be removed 20261001.
        public async Task MatchmakingJoinLobby()
        {
            using (var db = databaseFactory.GetInstance())
            {
                // Since this is only a compatibility method, we don't really care WHICH lobby is joined, as long as it's one of the active pools.
                matchmaking_pool? pool = (await db.GetActiveMatchmakingPoolsAsync()).FirstOrDefault();

                if (pool == null)
                    return;

                await MatchmakingJoinLobbyWithParams(new MatchmakingJoinLobbyRequest { PoolId = (int)pool.id });
            }
        }

        public async Task<MatchmakingPool[]> GetMatchmakingPoolsOfType(MatchmakingPoolType type)
        {
            using (var db = databaseFactory.GetInstance())
            {
                return (await db.GetActiveMatchmakingPoolsAsync())
                       .Select(p => p.ToMatchmakingPool())
                       .Where(p => p.Type == type)
                       .ToArray();
            }
        }

        public async Task<MatchmakingJoinLobbyResponse> MatchmakingJoinLobbyWithParams(MatchmakingJoinLobbyRequest request)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.AddToLobbyAsync(userUsage.Item!, request.PoolId);

            return new MatchmakingJoinLobbyResponse();
        }

        public async Task MatchmakingLeaveLobby()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.RemoveFromLobbyAsync(userUsage.Item!);
        }

        public async Task MatchmakingJoinQueue(int poolId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't queue when restricted.");
            }

            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.AddToQueueAsync(userUsage.Item!, poolId);
        }

        public async Task MatchmakingLeaveQueue()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.RemoveFromQueueAsync(userUsage.Item!);
        }

        public async Task MatchmakingAcceptInvitation()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.AcceptInvitationAsync(userUsage.Item!);
        }

        public async Task<MatchmakingIssueDuelResponse> MatchmakingIssueDuel(MatchmakingIssueDuelRequest request)
        {
            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't duel when restricted.");
            }

            await checkUserToUserPermissionsAsync(request.UserId);

            using (var userUsage = await GetOrCreateLocalUserState())
                return await matchmakingQueueService.IssueDuelAsync(userUsage.Item!, request);
        }

        public async Task<MatchmakingAcceptDuelResponse> MatchmakingAcceptDuel(MatchmakingAcceptDuelRequest request)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                return await matchmakingQueueService.AcceptDuelAsync(userUsage.Item!, request);
        }

        public async Task MatchmakingDeclineInvitation()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.DeclineInvitationAsync(userUsage.Item!);
        }

        public async Task MatchmakingToggleSelection(long playlistItemId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item!))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                await room.MatchmakingToggleSelection(Context.GetUserId(), playlistItemId);
            }
        }

        public async Task MatchmakingSkipToNextStage()
        {
            // This is only used for testing purposes right now.
            // It causes the room to skip forward with *any* user's request, which will not work well in standard usage.
            if (!AppSettings.MatchmakingRoomAllowSkip)
                throw new InvalidStateException("Skipping matchmaking rounds is not allowed.");

            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item!))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                room.MatchmakingSkipToNextStage(Context.GetUserId(), out _);
            }
        }
    }
}
