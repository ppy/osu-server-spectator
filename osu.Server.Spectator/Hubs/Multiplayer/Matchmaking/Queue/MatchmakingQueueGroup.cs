// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Retrieves the distribution of all rating differences in this group.
        /// </summary>
        public double[] DeltaRatings()
        {
            List<double> deltaRatings = new List<double>();

            for (int i = 0; i < Users.Length; i++)
            {
                for (int j = i + 1; j < Users.Length; j++)
                    deltaRatings.Add(Math.Abs(Users[i].Rating.Mu - Users[j].Rating.Mu));
            }

            return deltaRatings.ToArray();
        }

        public bool Equals(MatchmakingQueueGroup? other)
            => other != null && Identifier == other.Identifier;

        public override bool Equals(object? obj)
            => obj is MatchmakingQueueGroup other && Equals(other);

        public override int GetHashCode()
            => Identifier.GetHashCode();
    }
}
