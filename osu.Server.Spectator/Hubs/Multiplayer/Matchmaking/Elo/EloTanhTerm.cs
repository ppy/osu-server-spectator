// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
//
// Portions of this file are adapted from Elo-MMR (https://github.com/EbTech/Elo-MMR)
// See THIRD_PARTY_LICENCES in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo
{
    [Serializable]
    public struct EloTanhTerm
    {
        private static readonly double tanh_multiplier = Math.PI / Math.Sqrt(3);

        [JsonProperty("mu")]
        public double Mu { get; set; }

        [JsonProperty("warg")]
        public double Warg { get; set; }

        [JsonProperty("wout")]
        public double Wout { get; set; }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/mod.rs#L46
        public EloTanhTerm(EloRating rating)
        {
            double w = tanh_multiplier / rating.Sig;
            Mu = rating.Mu;
            Warg = w * 0.5;
            Wout = w;
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/mod.rs#L61
        public double GetWeight()
        {
            return Wout * Warg * 2 / Math.Pow(tanh_multiplier, 2);
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/mod.rs#L61
        public (double, double) GetBaseValues(double x)
        {
            double z = (x - Mu) * Warg;
            double val = -Math.Tanh(z) * Wout;
            double valPrime = -Math.Pow(Math.Cosh(z), 2) * Warg * Wout;
            return (val, valPrime);
        }
    }
}
