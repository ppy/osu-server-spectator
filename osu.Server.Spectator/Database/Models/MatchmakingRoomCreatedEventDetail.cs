// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable InconsistentNaming

    public class MatchmakingRoomCreatedEventDetail
    {
        [JsonProperty("pool_id")]
        public int pool_id { get; set; }
    }
}
