// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Matchmaking;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : IMatchmakingServer
    {
        public async Task<MatchmakingPool[]> GetMatchmakingPools()
        {
            using (var db = databaseFactory.GetInstance())
                return (await db.GetActiveMatchmakingPoolsAsync()).Select(p => p.ToMatchmakingPool()).ToArray();
        }

        public async Task MatchmakingJoinLobby()
        {
            using (await GetOrCreateLocalUserState())
                await matchmakingQueueService.AddToLobbyAsync(new MatchmakingClientState(Context));
        }

        public async Task MatchmakingLeaveLobby()
        {
            using (await GetOrCreateLocalUserState())
                await matchmakingQueueService.RemoveFromLobbyAsync(new MatchmakingClientState(Context));
        }

        public async Task MatchmakingJoinQueue(int poolId)
        {
            using (await GetOrCreateLocalUserState())
            {
                await matchmakingQueueService.AddToQueueAsync(new MatchmakingClientState(Context), poolId);
            }
        }

        public async Task MatchmakingLeaveQueue()
        {
            using (await GetOrCreateLocalUserState())
                await matchmakingQueueService.RemoveFromQueueAsync(new MatchmakingClientState(Context));
        }

        public async Task MatchmakingAcceptInvitation()
        {
            using (await GetOrCreateLocalUserState())
                await matchmakingQueueService.AcceptInvitationAsync(new MatchmakingClientState(Context));
        }

        public async Task MatchmakingDeclineInvitation()
        {
            using (await GetOrCreateLocalUserState())
                await matchmakingQueueService.DeclineInvitationAsync(new MatchmakingClientState(Context));
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

                ((MatchmakingMatchController)room.Controller).SkipToNextStage(out _);
            }
        }
    }
}
