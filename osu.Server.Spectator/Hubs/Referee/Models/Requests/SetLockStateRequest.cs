// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Sets the room's lock state.
    /// In a locked room, players cannot change their own teams, only referees can move players across teams.
    /// </summary>
    [PublicAPI]
    public class SetLockStateRequest
    {
        /// <summary>
        /// Whether the room should be locked.
        /// </summary>
        [JsonPropertyName("locked")]
        public bool Locked { get; set; }
    }
}
