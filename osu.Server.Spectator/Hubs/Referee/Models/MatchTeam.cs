// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    /// <summary>
    /// Enumerates possible teams in rooms.
    /// </summary>
    [PublicAPI]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MatchTeam
    {
        [JsonStringEnumMemberName("red")]
        Red = 0,

        [JsonStringEnumMemberName("blue")]
        Blue = 1,
    }
}
