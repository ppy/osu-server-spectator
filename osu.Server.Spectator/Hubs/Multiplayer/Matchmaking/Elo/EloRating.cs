// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo
{
    [Serializable]
    public struct EloRating
    {
        [JsonProperty("mu")]
        public double Mu { get; set; } = 1500;

        [JsonProperty("sig")]
        public double Sig { get; set; } = 350;

        public EloRating()
        {
        }

        public EloRating(double mu, double sig)
        {
            Mu = mu;
            Sig = sig;
        }

        // https://github.com/EbTech/Elo-MMR/blob/f6f83bb2c54bf173e60a9e8614065e8d168a349b/multi-skill/src/systems/common/mod.rs#L19
        public EloRating WithNoise(double sigNoise)
        {
            double newSig = Math.Sqrt(Sig * Sig + sigNoise * sigNoise);
            return this with { Sig = newSig };
        }
    }
}
