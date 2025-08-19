// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingQueue
    {
        /// <summary>
        /// The required room size;
        /// </summary>
        public int RoomSize { get; set; } = MatchmakingImplementation.MATCHMAKING_ROOM_SIZE;

        /// <summary>
        /// The time before users are automatically removed from the queue if they haven't accepted the invitation.
        /// </summary>
        public TimeSpan InviteTimeout { get; set; } = TimeSpan.FromSeconds(30);

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
        /// Adds a user to the matchmaking queue.
        /// </summary>
        /// <param name="user">The user to add.</param>
        public MatchmakingQueueUpdateBundle Add(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle();

            lock (queueLock)
            {
                if (matchmakingUsers.Add(user))
                    bundle.AddedUsers.Add((user, false));
            }

            return bundle;
        }

        /// <summary>
        /// Removes a user from the matchmaking queue.
        /// </summary>
        /// <param name="user">The user to remove.</param>
        public MatchmakingQueueUpdateBundle Remove(MatchmakingQueueUser user)
        {
            var bundle = new MatchmakingQueueUpdateBundle();

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
            var bundle = new MatchmakingQueueUpdateBundle();

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
                {
                    matchmakingUsers.Remove(u);
                    bundle.RemovedUsers.Add(u);
                }

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
            var bundle = new MatchmakingQueueUpdateBundle();

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
            var bundle = new MatchmakingQueueUpdateBundle();

            lock (queueLock)
            {
                foreach (MatchmakingQueueUser[] users in groupAvailableUsers())
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
            var bundle = new MatchmakingQueueUpdateBundle();

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

                        bundle.AddedUsers.Add((user, true));
                    }
                }
            }

            return bundle;
        }

        private IEnumerable<MatchmakingQueueUser[]> groupAvailableUsers()
        {
            return matchmakingUsers
                   .Where(u => u.Group == null)
                   .OrderByDescending(u => u.Rank)
                   .Chunk(RoomSize);
        }
    }
}
