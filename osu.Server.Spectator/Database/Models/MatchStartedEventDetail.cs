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

        [JsonProperty("slots")]
        public Dictionary<int, byte>? slots { get; set; }
    }

    /// <remarks>
    /// Note that the numerical values of this enum DO NOT match stable / legacy convention as defined in
    /// <see href="https://github.com/peppy/osu-stable-reference/blob/3ea48705eb67172c430371dcfc8a16a002ed0d3d/osu!common/SharedEnums.cs#L48-L53">stable</see>
    /// and <see href="https://github.com/ppy/osu-web/blob/master/app/Models/LegacyMatch/Score.php#L36-L40">osu-web</see>.
    /// This is fine, however, as the raw numerical value is only used as a way to convert from the new lazer convention as defined
    /// <see href="https://github.com/ppy/osu/blob/ed905b761dbba26156dbd089286b5e77cba389dd/osu.Game/Online/Multiplayer/MatchTypes/TeamVersus/TeamVersusRoomState.cs#L23-L24">here</see>;
    /// the actual value that gets written out to database is the stringified representation of the enum due to the attached <see cref="StringEnumConverter"/>.
    /// </remarks>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum room_team
    {
        red = 0,
        blue = 1,
    }
}
