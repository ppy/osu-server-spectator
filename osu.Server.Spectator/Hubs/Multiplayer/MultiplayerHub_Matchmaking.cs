// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : IMatchmakingServer
    {
        public async Task<MatchmakingPool[]> GetMatchmakingPools(MatchmakingPoolType type)
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

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                await ((MatchmakingMatchController)room.Controller).ToggleSelectionAsync(user, playlistItemId);
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

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                ((IMatchmakingMatchController)room.Controller).SkipToNextStage(out _);
            }
        }
    }
}
