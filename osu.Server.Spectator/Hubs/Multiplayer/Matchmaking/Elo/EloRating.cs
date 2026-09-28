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
    public struct EloRating
    {
        /// <summary>
        /// The minimum allowable rating mu, as required by rating tiers.
        /// </summary>
        private const double min_mu = 600;

        [JsonProperty("mu")]
        public double Mu { get; } = 1500;

        [JsonProperty("sig")]
        public double Sig { get; } = 150;

        public EloRating()
        {
        }

        public EloRating(double mu)
        {
            Mu = Math.Max(min_mu, mu);
        }

        [JsonConstructor]
        public EloRating(double mu, double sig)
            : this(mu)
        {
            Sig = sig;
        }
    }
}
