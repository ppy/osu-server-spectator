// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable InconsistentNaming

    [Serializable]
    public class MatchStartedEventDetail
    {
        [JsonProperty("room_type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public database_match_type room_type { get; set; }

        [JsonProperty("teams")]
        public Dictionary<int, room_team>? teams { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum room_team
    {
        blue = 1,
        red = 2,
    }
}
