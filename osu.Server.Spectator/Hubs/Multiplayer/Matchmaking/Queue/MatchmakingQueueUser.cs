// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    /// <summary>
    /// An active matchmaking user.
    /// </summary>
    public class MatchmakingQueueUser : IEquatable<MatchmakingQueueUser>
    {
        /// <summary>
        /// This user's rank.
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// The time at which this user was invited to the matchmaking room.
        /// </summary>
        public DateTimeOffset InviteStartTime { get; set; }

        /// <summary>
        /// Whether this user has accepted the matchmaking room invitation.
        /// </summary>
        public bool InviteAccepted { get; set; }

        /// <summary>
        /// The group which this user has been matched with.
        /// </summary>
        public MatchmakingQueueGroup? Group { get; set; }

        /// <summary>
        /// A unique identifier for this user.
        /// </summary>
        public readonly string Identifier;

        public MatchmakingQueueUser(string identifier)
        {
            Identifier = identifier;
        }

        public bool Equals(MatchmakingQueueUser? other)
            => other != null && Identifier == other.Identifier;

        public override bool Equals(object? obj)
            => obj is MatchmakingQueueUser other && Equals(other);

        public override int GetHashCode()
            => Identifier.GetHashCode();
    }
}
