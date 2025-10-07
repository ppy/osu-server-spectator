// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
//
// Portions of this file are adapted from Elo-MMR (https://github.com/EbTech/Elo-MMR)
// See THIRD_PARTY_LICENCES in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo
{
    public class EloSystem
    {
        /// <summary>
        /// The weight of each new contest.
        /// </summary>
        public double WeightLimit = 0.2;

        /// <summary>
        /// Weight multipliers (&lt;=1) to apply on the first few contests.
        /// </summary>
        public double[] NoobDelay = [];

        /// <summary>
        /// Each contest participation adds an amount of drift such that, in the absence of much time passing,
        /// the limiting skill uncertainty's square approaches this value.
        /// </summary>
        public double SigLimit = 80;

        /// <summary>
        /// Additional variance per day.
        /// </summary>
        public double DriftPerDay;

        /// <summary>
        /// Maximum number of opponents and recent events to use.
        /// </summary>
        public double TransferSpeed = 1;

        /// <summary>
        /// Maximum number of recent contests to store.
        /// </summary>
        public int MaxHistory = int.MaxValue;

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/simple_elo_mmr.rs#L94
        public void RecordContest(EloContest contest)
        {
            EloTanhTerm[] tanhTerms = new EloTanhTerm[contest.Standings.Length];

            for (int i = 0; i < contest.Standings.Length; i++)
            {
                EloPlayer player = contest.Standings[i];

                TimeSpan deltaTime = contest.Time - player.LastContestTime ?? TimeSpan.Zero;
                double weight = computeWeight(contest.Weight, player.ContestCount);
                double sigPerf = computeSigPerf(weight);
                double sigDrift = computeSigDrift(weight, deltaTime);
                player.AddNoiseBest(sigDrift, TransferSpeed);

                tanhTerms[i] = new EloTanhTerm(player.ApproximatePosterior.WithNoise(sigPerf));
            }

            for (int i = 0; i < contest.Standings.Length; i++)
            {
                EloPlayer player = contest.Standings[i];

                int index = i;
                double muPerf = Math.Min(contest.PerformanceCeiling, SolveNewton(x => ComputeLikelihoodSum(x, tanhTerms, index, index)));
                double weight = computeWeight(contest.Weight, player.ContestCount);
                double sigPerf = computeSigPerf(weight);
                player.UpdatePerformance(contest.Time, new EloRating(muPerf, sigPerf), MaxHistory);
            }
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/simple_elo_mmr.rs#L57
        private double computeWeight(double contestWeight, int contestIndex)
        {
            contestWeight *= WeightLimit;
            if (contestIndex < NoobDelay.Length)
                contestWeight *= NoobDelay[contestIndex];
            return contestWeight;
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/simple_elo_mmr.rs#L65
        private double computeSigPerf(double weight)
        {
            double discretePerf = (1.0 + 1.0 / weight) * SigLimit * SigLimit;
            double continuousPerf = DriftPerDay / weight;
            return Math.Sqrt(discretePerf + continuousPerf);
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/simple_elo_mmr.rs#L71
        private double computeSigDrift(double weight, TimeSpan deltaTime)
        {
            double discreteDrift = weight * SigLimit * SigLimit;
            double continuousDrift = DriftPerDay * deltaTime.TotalDays;
            return Math.Sqrt(discreteDrift + continuousDrift);
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/numerical.rs#L77
        public static double SolveNewton(Func<double, (double, double)> iterator, double low = -6000, double high = 9000)
        {
            double guess = 0.5 * (low + high);

            while (true)
            {
                (double sum, double sumPrime) = iterator(guess);
                double extrapolate = guess - sum / sumPrime;

                if (extrapolate < guess)
                {
                    high = guess;
                    guess = Math.Max(extrapolate, high - 0.75 * (high - low));
                }
                else
                {
                    low = guess;
                    guess = Math.Min(extrapolate, low + 0.75 * (high - low));
                }

                if (low >= guess || guess >= high)
                    return guess;
            }
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/mod.rs#L73
        public static double RobustAverage(IReadOnlyList<EloTanhTerm> allRatings, double offset, double slope)
        {
            return SolveNewton(f);

            (double, double) f(double x)
            {
                double s = 0;
                double sp = 0;

                foreach (var term in allRatings)
                {
                    double tanhZ = Math.Tanh((x - term.Mu) * term.Warg);
                    s += tanhZ * term.Wout;
                    sp += (1.0 - tanhZ * tanhZ) * term.Warg * term.Wout;
                }

                return (s + offset + slope * x, sp + slope);
            }
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/simple_elo_mmr.rs#L118-L127
        public static (double, double) ComputeLikelihoodSum(double x, EloTanhTerm[] tanhTerms, int low, int high)
        {
            double s = 0;
            double sp = 0;

            for (int i = 0; i < tanhTerms.Length; i++)
            {
                (double s, double sp) value;

                if (i < low)
                    value = evalLess(tanhTerms[i], x);
                else if (i <= high)
                    value = evalEqual(tanhTerms[i], x);
                else
                    value = evalGreater(tanhTerms[i], x);

                s += value.s;
                sp += value.sp;
            }

            return (s, sp);

            static (double, double) evalLess(EloTanhTerm term, double x)
            {
                (double val, double valPrime) = term.GetBaseValues(x);
                return (val - term.Wout, valPrime);
            }

            static (double, double) evalEqual(EloTanhTerm term, double x)
            {
                (double val, double valPrime) = term.GetBaseValues(x);
                return (val * 2, valPrime * 2);
            }

            static (double, double) evalGreater(EloTanhTerm term, double x)
            {
                (double val, double valPrime) = term.GetBaseValues(x);
                return (val + term.Wout, valPrime);
            }
        }
    }
}
