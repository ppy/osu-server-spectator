// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;
using osu.Game.IO.Serialization.Converters;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    [JsonConverter(typeof(SnakeCaseStringEnumConverter))]
    public enum MatchType
    {
        HeadToHead = Game.Online.Rooms.MatchType.HeadToHead,
        TeamVersus = Game.Online.Rooms.MatchType.TeamVersus,
    }
}
