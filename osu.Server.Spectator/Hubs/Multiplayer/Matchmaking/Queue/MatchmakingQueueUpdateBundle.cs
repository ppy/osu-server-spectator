// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    /// <summary>
    /// Describes a set of updates to the matchmaking queue.
    /// </summary>
    public class MatchmakingQueueUpdateBundle
    {
        /// <summary>
        /// The queue which was updated.
        /// </summary>
        public readonly MatchmakingQueue Queue;

        /// <summary>
        /// Groups that were newly formed from users matching the search criteria.
        /// </summary>
        public readonly List<MatchmakingQueueGroup> FormedGroups = [];

        /// <summary>
        /// Groups where all players have accepted their invitations.
        /// </summary>
        public readonly List<MatchmakingQueueGroup> CompletedGroups = [];

        /// <summary>
        /// Groups where any players have not accepted their invitations.
        /// </summary>
        public readonly List<MatchmakingQueueGroup> RecycledGroups = [];

        /// <summary>
        /// Users that have joined the queue.
        /// </summary>
        public readonly List<MatchmakingQueueUser> AddedUsers = [];

        /// <summary>
        /// Users that have left the queue, either by requesting to leave the queue or declining an invitation.
        /// </summary>
        public readonly List<MatchmakingQueueUser> RemovedUsers = [];

        /// <summary>
        /// Users that have declined the queue. The contents of this array are duplicated into <see cref="RemovedUsers"/>.
        /// </summary>
        public readonly List<MatchmakingQueueUser> DeclinedUsers = [];

        public MatchmakingQueueUpdateBundle(MatchmakingQueue queue)
        {
            Queue = queue;
        }

        public void Append(MatchmakingQueueUpdateBundle other)
        {
            FormedGroups.AddRange(other.FormedGroups);
            CompletedGroups.AddRange(other.CompletedGroups);
            RecycledGroups.AddRange(other.RecycledGroups);
            AddedUsers.AddRange(other.AddedUsers);
            RemovedUsers.AddRange(other.RemovedUsers);
            DeclinedUsers.AddRange(other.DeclinedUsers);
        }
    }
}
