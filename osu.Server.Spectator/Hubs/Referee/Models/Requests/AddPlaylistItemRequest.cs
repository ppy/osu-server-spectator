// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Adds a new playlist item.
    /// </summary>
    [PublicAPI]
    public class AddPlaylistItemRequest
    {
        /// <summary>
        /// The ruleset of the new playlist item.
        /// </summary>
        [JsonPropertyName("ruleset_id")]
        public int RulesetId { get; set; }

        /// <summary>
        /// The beatmap of the new playlist item.
        /// </summary>
        [JsonPropertyName("beatmap_id")]
        public int BeatmapId { get; set; }

        /// <summary>
        /// The required mods of the new playlist item.
        /// </summary>
        [JsonPropertyName("required_mods")]
        public IEnumerable<Mod> RequiredMods { get; set; } = [];

        /// <summary>
        /// The allowed mods of the new playlist item.
        /// </summary>
        [JsonPropertyName("allowed_mods")]
        public IEnumerable<Mod> AllowedMods { get; set; } = [];

        /// <summary>
        /// The new freestyle state of the playlist item.
        /// </summary>
        [JsonPropertyName("freestyle")]
        public bool Freestyle { get; set; }
    }
}
