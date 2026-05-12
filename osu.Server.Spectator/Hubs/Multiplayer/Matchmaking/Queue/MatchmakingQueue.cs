// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using osu.Game.Extensions;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingQueue
    {
        /// <summary>
        /// The pool for this queue.
        /// </summary>
        public matchmaking_pool Pool { get; private set; }

        /// <summary>
        /// The time before users are automatically removed from the queue if they haven't accepted the invitation.
        /// </summary>
        public TimeSpan InviteTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The time before users are removed from the queue if they haven't been paired up.
        /// </summary>
        public TimeSpan SearchTimeout { get; set; } = TimeSpan.MaxValue;

        /// <summary>
        /// The system clock.
        /// </summary>
        public ISystemClock Clock { get; set; } = new SystemClock();

        /// <summary>
        /// All users active in the matchmaking queue.
        /// </summary>
        private readonly HashSet<MatchmakingQueueUser> matchmakingUsers = new HashSet<MatchmakingQueueUser>();

        /// <summary>
        /// Lock for <see cref="matchmakingUsers"/>.
        /// </summary>
        private readonly object queueLock = new object();

        /// <summary>
        /// The top-100 player's rating from the pool. This is populated upon the first <see cref="Refresh"/>.
        /// </summary>
        private int top100Rating = 99999;

        /// <summary>
        /// A running counter for the next group ID.
        /// </summary>
        private uint nextGroupId = 1;

        public MatchmakingQueue(matchmaking_pool pool)
        {
            Pool = pool;
        }

        /// <summary>
        /// Retrieves the count of users in this queue.
        /// </summary>
        public int Count
        {
            get
            {
                lock (queueLock)
                    return matchmakingUsers.Count;
            }
        }

        /// <summary>
        /// Refreshes this <see cref="MatchmakingQueue"/> with a new pool.
        /// </summary>
        public async Task<MatchmakingQueueUpdateBundle> Refresh(IDatabaseAccess db)
        {
            matchmaking_pool? newPool = await db.GetMatchmakingPoolAsync(Pool.id);

            if (newPool == null)
                return Clear();

            Pool = newPool;

            if (!newPool.active)
                return Clear();

            int[] topRatings = await db.GetMatchmakingPoolTop100RatingsAsync(Pool.id);
            top100Rating = topRatings.LastOrDefault();

            return new MatchmakingQueueUpdateBundle(this);
        }

        /// <summary>
        /// Determines whether a given user is in the matchmaking queue.
        /// When a user is in the queue, they could either be waiting to be matched up or waiting for their group to be complete.
        /// </summary>
        /// <param name="user">The user to check.</param>
        /// <returns>Whether the given user is in the matchmaking queue.</returns>
        public bool IsInQueue(MatchmakingQueueUser user)
        {
            lock (queueLock)
                return matchmakingUsers.Contains(user);
        }

        /// <summary>
        /// Retrieves all users currently in the matchmaking queue.
        /// </summary>
        public MatchmakingQueueUser[] GetAllUsers()
        {
            lock (queueLock)
                return matchmakingUsers.ToArray();
        }

        /// <summary>
        /// Adds a user to the matchmaking queue.
        /// </summary>
        /// <param name="user">The user to add.</param>
        public MatchmakingQueueUpdateBundle Add(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                if (matchmakingUsers.Add(user))
                {
                    user.SearchStartTime = Clock.UtcNow;
                    bundle.AddedUsers.Add(user);
                }
            }

            return bundle;
        }

        /// <summary>
        /// Removes a user from the matchmaking queue.
        /// </summary>
        /// <param name="user">The user to remove.</param>
        public MatchmakingQueueUpdateBundle Remove(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                if (!matchmakingUsers.TryGetValue(user, out user!))
                    return bundle;

                bundle.Append(removeFromQueue([user], true));
            }

            return bundle;
        }

        /// <summary>
        /// Clears the matchmaking queue, removing all users.
        /// </summary>
        public MatchmakingQueueUpdateBundle Clear()
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                if (matchmakingUsers.Count == 0)
                    return bundle;

                bundle.Append(removeFromQueue(matchmakingUsers.ToArray(), false));
            }

            return bundle;
        }

        /// <summary>
        /// Marks a user as having accepted their invitation to the match.
        /// Groups are formed when all players have accepted their invitations.
        /// </summary>
        /// <param name="user">The user to mark as having accepted their invitation.</param>
        public MatchmakingQueueUpdateBundle MarkInvitationAccepted(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                if (!matchmakingUsers.TryGetValue(user, out user!))
                    return bundle;

                if (user.Group == null)
                    return bundle;

                user.InviteAccepted = true;

                if (user.Group.Users.Any(u => !u.InviteAccepted))
                    return bundle;

                foreach (var u in user.Group.Users)
                    matchmakingUsers.Remove(u);

                bundle.CompletedGroups.Add(user.Group);
            }

            return bundle;
        }

        /// <summary>
        /// Marks a user user as having declined their invitation to the match.
        /// All other users in their respective group will be returned to the matchmaking queue.
        /// </summary>
        /// <param name="user">The user to mark as having declined their invitation.</param>
        public MatchmakingQueueUpdateBundle MarkInvitationDeclined(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                // Re-fetch the user from the queue.
                if (!matchmakingUsers.TryGetValue(user, out user!))
                    return bundle;

                bundle.Append(removeFromQueue([user], true));
            }

            return bundle;
        }

        /// <summary>
        /// Performs a single update of the matchmaking queue.
        /// </summary>
        public MatchmakingQueueUpdateBundle Update()
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                foreach (MatchmakingQueueUser[] users in matchUsers())
                {
                    if (users.Length < Pool.lobby_size)
                        break;

                    MatchmakingQueueGroup group = new MatchmakingQueueGroup($"matchmaking-{nextGroupId++}", users);

                    foreach (var user in users)
                    {
                        user.InviteStartTime = Clock.UtcNow;
                        user.Group = group;
                    }

                    bundle.FormedGroups.Add(group);
                }

                List<MatchmakingQueueUser> inviteTimeoutUsers = [];

                foreach (var user in matchmakingUsers.Where(u => u.Group != null && !u.InviteAccepted))
                {
                    if (Clock.UtcNow - user.InviteStartTime > InviteTimeout)
                        inviteTimeoutUsers.Add(user);
                }

                bundle.Append(removeFromQueue(inviteTimeoutUsers, true));

                List<MatchmakingQueueUser> searchTimeoutUsers = [];

                foreach (var user in matchmakingUsers.Where(u => u.Group == null))
                {
                    if (Clock.UtcNow - user.SearchStartTime > SearchTimeout)
                        searchTimeoutUsers.Add(user);
                }

                bundle.Append(removeFromQueue(searchTimeoutUsers, false));
            }

            return bundle;
        }

        /// <summary>
        /// Removes one or more users from the queue.
        /// </summary>
        /// <param name="users">The users to remove from the queue. This should be used to batch users whenever possible.</param>
        /// <param name="markAsDeclined">For users have been invited to rooms, whether to mark them as having declined their invitations.</param>
        private MatchmakingQueueUpdateBundle removeFromQueue(IList<MatchmakingQueueUser> users, bool markAsDeclined)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                foreach (var user in users)
                {
                    matchmakingUsers.Remove(user);

                    if (user.Group != null && markAsDeclined)
                        bundle.DeclinedUsers.Add(user);

                    bundle.RemovedUsers.Add(user);
                }

                foreach (var group in users.Where(u => u.Group != null).GroupBy(u => u.Group))
                {
                    bundle.RecycledGroups.Add(group.Key!);

                    foreach (var user in group.Key!.Users)
                    {
                        if (!matchmakingUsers.Contains(user))
                            continue;

                        user.Group = null;
                        user.InviteAccepted = false;

                        bundle.AddedUsers.Add(user);
                    }
                }
            }

            return bundle;
        }

        /// <summary>
        /// Forms <see cref="matchmaking_pool.lobby_size"/> groups of users of similar rating.
        /// </summary>
        private IEnumerable<MatchmakingQueueUser[]> matchUsers()
        {
            List<MatchmakingQueueUser> availableUsers = matchmakingUsers.Where(u => u.Group == null)
                                                                        .Where(u => u.BanEndTime < Clock.UtcNow)
                                                                        .OrderBy(u => u.Rating.Mu)
                                                                        .ToList();

            if (availableUsers.Count < Pool.lobby_size)
                return [];

            List<MatchmakingQueueUser[]> results = new List<MatchmakingQueueUser[]>();

            for (int i = 0; i < availableUsers.Count; i++)
            {
                HashSet<MatchmakingQueueUser> matches = findMatchesForUser(availableUsers, i);

                if (matches.Count < Pool.lobby_size)
                    continue;

                availableUsers.RemoveAll(matches.Contains);
                results.Add(matches.ToArray());
            }

            return results;
        }

        /// <summary>
        /// Finds up to <see cref="matchmaking_pool.lobby_size"/> users within a similar rating of a given user.
        /// </summary>
        /// <param name="users">The users in the matchmaking queue.</param>
        /// <param name="pivotIndex">The index of the user in <paramref name="users"/> to match.</param>
        /// <returns></returns>
        private HashSet<MatchmakingQueueUser> findMatchesForUser(IReadOnlyList<MatchmakingQueueUser> users, int pivotIndex)
        {
            // Gradually expand a search from the pivot user until the rating search radius is exhausted.
            MatchmakingQueueUser pivotUser = users[pivotIndex];

            // Search bonus based on the user's rating to cover gaps in the rating distribution.
            double ratingBonus = Math.Exp(Math.Pow((pivotUser.Rating.Mu - 1500) / 750, 2));

            // Search bonus based on how much time the user spent in the queue.
            TimeSpan searchTime = Clock.UtcNow - pivotUser.SearchStartTime;
            double searchTimeBonus = Math.Pow(2, searchTime.TotalSeconds / Pool.rating_search_radius_exp);

            // Distance bonus such that top-100 players can always match against each other.
            double top100Bonus = Math.Max(1, (pivotUser.Rating.Mu - top100Rating) / Pool.rating_search_radius_max);

            double searchRadius = Math.Min(
                Pool.rating_search_radius_max * top100Bonus,
                Pool.rating_search_radius * ratingBonus * searchTimeBonus);

            IEnumerable<MatchmakingQueueUser> allMatches = users.Where(u => !u.Equals(pivotUser) && Math.Abs(pivotUser.Rating.Mu - u.Rating.Mu) <= searchRadius);

            HashSet<MatchmakingQueueUser> result = [pivotUser];
            result.AddRange(
                allMatches
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(Pool.lobby_size - 1) // pivotUser is already in collection, so we need the number of opponents
            );

            return result;
        }
    }
}
