// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Rulesets.Scoring;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class SoloScoreData
    {
        [JsonProperty("mods")]
        public APIMod[] Mods { get; set; } = Array.Empty<APIMod>();

        [JsonProperty("statistics")]
        public Dictionary<HitResult, int> Statistics { get; set; } = new Dictionary<HitResult, int>();

        [JsonProperty("maximum_statistics")]
        public Dictionary<HitResult, int> MaximumStatistics { get; set; } = new Dictionary<HitResult, int>();
    }
}
