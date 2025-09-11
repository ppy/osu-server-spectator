// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo
{
    public class EloContest
    {
        /// <summary>
        /// The time at which this contest occurred.
        /// </summary>
        public readonly DateTimeOffset Time;

        /// <summary>
        /// The contest standings.
        /// </summary>
        public readonly EloPlayer[] Standings;

        /// <summary>
        /// The relative weight of this contest.
        /// </summary>
        public double Weight { get; init; } = 1;

        /// <summary>
        /// The maximum performance this contest is intended to measure.
        /// </summary>
        public double PerformanceCeiling { get; init; } = double.PositiveInfinity;

        /// <summary>
        /// Creates a new <see cref="EloContest"/>.
        /// </summary>
        /// <param name="time">The time at which this contest occurred.</param>
        /// <param name="standings">The contest standings.</param>
        public EloContest(DateTimeOffset time, EloPlayer[] standings)
        {
            Time = time;
            Standings = standings;
        }
    }
}
