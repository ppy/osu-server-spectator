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
    public class EloPlayer
    {
        [JsonProperty("initial_rating")]
        public EloRating InitialRating { get; set; } = new EloRating();

        [JsonProperty("contest_count")]
        public int ContestCount { get; set; }

        [JsonProperty("approximate_posterior")]
        public EloRating Rating { get; set; } = new EloRating();
    }
}
