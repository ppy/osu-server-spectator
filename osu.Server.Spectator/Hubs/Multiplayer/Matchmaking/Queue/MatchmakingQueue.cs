// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingQueue
    {
        /// <summary>
        /// The pool this queue corresponds to.
        /// </summary>
        public readonly matchmaking_pool Pool;

        /// <summary>
        /// The required room size.
        /// </summary>
        public int RoomSize { get; set; } = AppSettings.MatchmakingRoomSize;

        /// <summary>
        /// The time before users are automatically removed from the queue if they haven't accepted the invitation.
        /// </summary>
        public TimeSpan InviteTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The initial ELO search radius.
        /// </summary>
        private const double elo_search_radius_start = 20;

        /// <summary>
        /// The amount of time (in seconds) before each doubling of the ELO search radius.
        /// </summary>
        /// <remarks>
        /// The search will start at 20 ELO difference and double every 15 seconds. After 90 seconds it will cover all possible users.
        /// </remarks>
        private const double elo_search_radius_double = 15;

        /// <summary>
        /// All users active in the matchmaking queue.
        /// </summary>
        private readonly HashSet<MatchmakingQueueUser> matchmakingUsers = new HashSet<MatchmakingQueueUser>();

        /// <summary>
        /// Lock for <see cref="matchmakingUsers"/>.
        /// </summary>
        private readonly object queueLock = new object();

        /// <summary>
        /// A running counter for the next group ID.
        /// </summary>
        private uint nextGroupId = 1;

        public MatchmakingQueue(matchmaking_pool pool)
        {
            Pool = pool;
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
        /// <returns></returns>
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
                    user.SearchStartTime = DateTimeOffset.Now;
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

                bundle.Append(markInvitationDeclined([user]));
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

                bundle.Append(markInvitationDeclined([user]));
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
                    if (users.Length < RoomSize)
                        break;

                    MatchmakingQueueGroup group = new MatchmakingQueueGroup($"matchmaking-{nextGroupId++}", users);

                    foreach (var user in users)
                    {
                        user.InviteStartTime = DateTimeOffset.Now;
                        user.Group = group;
                    }

                    bundle.FormedGroups.Add(group);
                }

                List<MatchmakingQueueUser> timedOutUsers = [];

                foreach (var user in matchmakingUsers.Where(u => u.Group != null && !u.InviteAccepted))
                {
                    if (DateTimeOffset.Now - user.InviteStartTime > InviteTimeout)
                        timedOutUsers.Add(user);
                }

                bundle.Append(markInvitationDeclined(timedOutUsers));
            }

            return bundle;
        }

        /// <summary>
        /// Marks users as having declined their invitation to the match.
        /// All other users in their respective groups will be returned to the matchmaking queue.
        /// </summary>
        /// <param name="users">The users to mark as having declined their invitation.</param>
        private MatchmakingQueueUpdateBundle markInvitationDeclined(IList<MatchmakingQueueUser> users)
        {
            var bundle = new MatchmakingQueueUpdateBundle(this);

            lock (queueLock)
            {
                foreach (var user in users)
                {
                    matchmakingUsers.Remove(user);
                    bundle.RemovedUsers.Add(user);
                }

                foreach (var group in users.Where(u => u.Group != null).GroupBy(u => u.Group))
                {
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
        /// Forms <see cref="RoomSize"/> groups of users of similar ELO.
        /// </summary>
        private IEnumerable<MatchmakingQueueUser[]> matchUsers()
        {
            List<MatchmakingQueueUser> availableUsers = matchmakingUsers.Where(u => u.Group == null)
                                                                        .OrderBy(u => u.Rating.Mu)
                                                                        .ToList();

            if (availableUsers.Count < RoomSize)
                return [];

            List<MatchmakingQueueUser[]> results = new List<MatchmakingQueueUser[]>();

            for (int i = 0; i < availableUsers.Count; i++)
            {
                HashSet<MatchmakingQueueUser> matches = findMatchesForUser(availableUsers, i);

                if (matches.Count < RoomSize)
                    continue;

                availableUsers.RemoveAll(matches.Contains);
                results.Add(matches.ToArray());
            }

            return results;
        }

        /// <summary>
        /// Finds up to <see cref="RoomSize"/> users within a similar ELO of a given user.
        /// </summary>
        /// <param name="users">The users in the matchmaking queue.</param>
        /// <param name="pivotIndex">The index of the user in <paramref name="users"/> to match.</param>
        /// <returns></returns>
        private HashSet<MatchmakingQueueUser> findMatchesForUser(IReadOnlyList<MatchmakingQueueUser> users, int pivotIndex)
        {
            const double uncertainty = 0;

            // Gradually expand a search from the pivot user until the ELO search radius is exhausted.

            MatchmakingQueueUser pivotUser = users[pivotIndex];
            HashSet<MatchmakingQueueUser> result = [pivotUser];

            int leftIndex = pivotIndex - 1;
            int rightIndex = pivotIndex + 1;

            while (result.Count < RoomSize)
            {
                MatchmakingQueueUser? leftUser = leftIndex < 0 ? null : users[leftIndex];
                MatchmakingQueueUser? rightUser = rightIndex >= users.Count ? null : users[rightIndex];

                if (leftUser == null && rightUser == null)
                    break;

                double leftDistance = leftUser != null
                    ? Math.Abs(Math.Abs(pivotUser.Rating.Mu - leftUser.Rating.Mu) - (pivotUser.Rating.Sig - leftUser.Rating.Sig) * uncertainty)
                    : double.MaxValue;

                double rightDistance = rightUser != null
                    ? Math.Abs(Math.Abs(pivotUser.Rating.Mu - rightUser.Rating.Mu) - (pivotUser.Rating.Sig - rightUser.Rating.Sig) * uncertainty)
                    : double.MaxValue;

                double distance = Math.Min(leftDistance, rightDistance);

                TimeSpan searchTime = DateTimeOffset.Now - pivotUser.SearchStartTime;
                double searchRadius = elo_search_radius_start * Math.Pow(2, searchTime.TotalSeconds / elo_search_radius_double);

                if (distance > searchRadius)
                    break;

                result.Add(leftDistance < rightDistance ? users[leftIndex--] : users[rightIndex++]);
            }

            return result;
        }
    }
}
