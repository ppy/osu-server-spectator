// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    /// <summary>
    /// An active matchmaking group.
    /// </summary>
    public class MatchmakingQueueGroup : IEquatable<MatchmakingQueueGroup>
    {
        /// <summary>
        /// A unique identifier for this group.
        /// </summary>
        public readonly string Identifier;

        /// <summary>
        /// The users that are part of this group.
        /// </summary>
        public readonly MatchmakingQueueUser[] Users;

        public MatchmakingQueueGroup(string identifier, MatchmakingQueueUser[] users)
        {
            Identifier = identifier;
            Users = users;
        }

        public bool Equals(MatchmakingQueueGroup? other)
            => other != null && Identifier == other.Identifier;

        public override bool Equals(object? obj)
            => obj is MatchmakingQueueGroup other && Equals(other);

        public override int GetHashCode()
            => Identifier.GetHashCode();
    }
}
