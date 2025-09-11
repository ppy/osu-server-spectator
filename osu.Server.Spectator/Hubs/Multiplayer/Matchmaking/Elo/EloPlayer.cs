// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
//
// Portions of this file are adapted from Elo-MMR (https://github.com/EbTech/Elo-MMR)
// See THIRD_PARTY_LICENCES in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo
{
    [Serializable]
    public class EloPlayer
    {
        [JsonProperty("contest_count")]
        public int ContestCount;

        [JsonProperty("last_contest_time")]
        public DateTimeOffset? LastContestTime;

        [JsonProperty("normal_factor")]
        public EloRating NormalFactor = new EloRating();

        [JsonProperty("approximate_posterior")]
        public EloRating ApproximatePosterior = new EloRating();

        [JsonProperty("logistic_factors")]
        public List<EloTanhTerm> LogisticFactors = [];

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/player.rs#L139
        public void AddNoiseBest(double sigNoise, double transferSpeed)
        {
            EloRating newPosterior = ApproximatePosterior.WithNoise(sigNoise);

            double decay = Math.Pow(ApproximatePosterior.Sig / newPosterior.Sig, 2);
            double transfer = Math.Pow(decay, transferSpeed);

            ApproximatePosterior = newPosterior;

            double weightNormOld = Math.Pow(NormalFactor.Sig, -2);
            double weightFromNormOld = transferSpeed * weightNormOld;
            double weightFromTransfer = (1 - transfer) * (weightNormOld + LogisticFactors.Aggregate<EloTanhTerm, double>(0, (acc, f) => acc + f.GetWeight()));
            double weightTotal = weightFromNormOld + weightFromTransfer;

            NormalFactor = new EloRating
            (
                (weightFromNormOld * NormalFactor.Mu + weightFromTransfer * ApproximatePosterior.Mu) / weightTotal,
                Math.Pow(decay * weightTotal, -0.5)
            );

            for (int i = 0; i < LogisticFactors.Count; i++)
            {
                EloTanhTerm factor = LogisticFactors[i];
                factor.Wout *= transfer * decay;
                LogisticFactors[i] = factor;
            }
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/player.rs#L84
        public void UpdatePerformance(DateTimeOffset contestTime, EloRating performance, int maxHistory)
        {
            while (LogisticFactors.Count >= maxHistory)
            {
                double wN = Math.Pow(NormalFactor.Sig, -2);
                double wL = LogisticFactors[0].GetWeight();

                NormalFactor = new EloRating
                (
                    (wN * NormalFactor.Mu + wL * LogisticFactors[0].Mu) / (wN + wL),
                    Math.Pow(wN + wL, -0.5)
                );

                LogisticFactors.RemoveAt(0);
            }

            LogisticFactors.Add(new EloTanhTerm(performance));

            double normalWeight = Math.Pow(NormalFactor.Sig, -2);
            double mu = EloSystem.RobustAverage(LogisticFactors, -NormalFactor.Mu * normalWeight, normalWeight);
            double sig = Math.Pow(Math.Pow(ApproximatePosterior.Sig, -2) + Math.Pow(performance.Sig, -2), -0.5);
            ApproximatePosterior = new EloRating(mu, sig);

            ContestCount += 1;
            LastContestTime = contestTime;
        }

        // This isn't quite correct
        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/player.rs#L15-L20
        public override string ToString() => $"{ApproximatePosterior.Mu} ± {3 * (ApproximatePosterior.Sig - 80)}"; // 80 is EloSystem.SigLimit
    }
}
