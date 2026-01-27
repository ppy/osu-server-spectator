// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : IMatchmakingServer
    {
        // Provided for backwards compatibility. Can be removed 20260727.
        public Task<MatchmakingPool[]> GetMatchmakingPools()
            => GetMatchmakingPoolsOfType(MatchmakingPoolType.QuickPlay);

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

        public async Task MatchmakingJoinLobby()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.AddToLobbyAsync(userUsage.Item!);
        }

        public async Task MatchmakingLeaveLobby()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
                await matchmakingQueueService.RemoveFromLobbyAsync(userUsage.Item!);
        }

        public async Task MatchmakingJoinQueue(int poolId)
        {
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
