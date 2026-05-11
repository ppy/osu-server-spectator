// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingLobby
    {
        /// <summary>
        /// Retrieves the matchmaking queue for a given pool ID.
        /// </summary>
        public required Func<int, MatchmakingQueue?> LookupQueue { get; init; }

        private readonly int poolId;
        private readonly IHubContext<MultiplayerHub> hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly string groupName;

        /// <summary>
        /// Any newly completed matches since the last global distribution.
        /// </summary>
        private readonly List<MatchRoomState> newlyCompletedMatches = [];

        /// <summary>
        /// The 50 most recently completed matches.
        /// </summary>
        private readonly List<MatchRoomState> recentlyCompletedMatches = [];

        /// <summary>
        /// The time at which the rating distribution array was last updated.
        /// </summary>
        private DateTimeOffset lastRatingDistributionRefreshTime = DateTimeOffset.UnixEpoch;

        /// <summary>
        /// This pool's rating distribution.
        /// </summary>
        private (int Rating, int Count)[] ratingDistribution = [];

        public MatchmakingLobby(int poolId, IHubContext<MultiplayerHub> hub, IDatabaseFactory dbFactory)
        {
            this.poolId = poolId;
            this.hub = hub;
            this.dbFactory = dbFactory;

            groupName = $"matchmaking-lobby-users:{poolId}";
        }

        public async Task Add(MultiplayerClientState state)
        {
            await hub.Groups.AddToGroupAsync(state.ConnectionId, groupName);
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), await buildStatusUpdate(state.UserId));
        }

        public async Task Remove(MultiplayerClientState state)
        {
            await hub.Groups.RemoveFromGroupAsync(state.ConnectionId, groupName);
        }

        public async Task Update()
        {
            await hub.Clients.Group(groupName).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), await buildStatusUpdate(null));
        }

        public Task RecordMatch(MatchRoomState state)
        {
            lock (newlyCompletedMatches)
                newlyCompletedMatches.Add(state);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a status update for the lobby to be distributed to clients.
        /// </summary>
        /// <param name="targetUserId">The target user's whose rating is to be included in the distribution.</param>
        /// <returns>The status update bundle.</returns>
        private async Task<MatchmakingLobbyStatus> buildStatusUpdate(int? targetUserId)
        {
            MatchRoomState[] matches;

            lock (newlyCompletedMatches)
            {
                // When arriving with no target user, this is an update to be sent globally to all users.
                // We only want to send new matches which have not been reported to the user previously.
                if (targetUserId == null)
                {
                    matches = newlyCompletedMatches.ToArray();
                    newlyCompletedMatches.Clear();

                    // Update the recent list with any new matches, ensuring we keep only 50 max.
                    recentlyCompletedMatches.AddRange(matches);
                    while (recentlyCompletedMatches.Count > 50)
                        recentlyCompletedMatches.RemoveAt(0);
                }

                else
                    matches = recentlyCompletedMatches.ToArray();
            }

            int? userRating = null;

            using (var db = dbFactory.GetInstance())
            {
                if (DateTimeOffset.Now - lastRatingDistributionRefreshTime >= TimeSpan.FromMinutes(5))
                {
                    Dictionary<int, int> ratingCounts = new Dictionary<int, int>();

                    foreach (int rating in await db.GetMatchmakingPoolRatingsAsync((uint)poolId))
                    {
                        int ratingRounded = (int)Math.Floor((float)rating / 25) * 25;
                        ratingCounts[ratingRounded] = ratingCounts.GetValueOrDefault(ratingRounded) + 1;
                    }

                    ratingDistribution = ratingCounts.OrderBy(kvp => kvp.Key).Select(kvp => (kvp.Key, kvp.Value)).ToArray();
                    lastRatingDistributionRefreshTime = DateTimeOffset.Now;
                }

                if (targetUserId != null)
                {
                    var userStats = await db.GetMatchmakingUserStatsAsync(targetUserId.Value, (uint)poolId);
                    userRating = userStats == null ? null : (int)Math.Round(userStats.EloData.Rating.Mu);
                }
            }

            MatchmakingQueue? queue = LookupQueue(poolId);
            MatchmakingQueueUser[] queuedUsers = queue?.GetAllUsers() ?? [];
            Random.Shared.Shuffle(queuedUsers);

            return new MatchmakingLobbyStatus
            {
                UsersInQueue = queuedUsers.Take(50).Select(u => u.UserId).ToArray(),
                RatingDistribution = ratingDistribution,
                UserRating = userRating,
                RecentMatches = matches
            };
        }
    }
}
