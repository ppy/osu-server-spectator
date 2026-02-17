// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Changes the current playlist item.
    /// </summary>
    public class EditCurrentPlaylistItemRequest
    {
        /// <summary>
        /// The new ruleset of the playlist item.
        /// Pass <see langword="null"/> to keep the previous one.
        /// </summary>
        [JsonPropertyName("ruleset_id")]
        public int? RulesetId { get; set; }

        /// <summary>
        /// The new beatmap of the playlist item.
        /// Pass <see langword="null"/> to keep the previous one.
        /// </summary>
        [JsonPropertyName("beatmap_id")]
        public int? BeatmapId { get; set; }

        /// <summary>
        /// The new required mods of the playlist item.
        /// Pass <see langword="null"/> to keep the previous ones.
        /// If <see cref="RulesetId"/> is also changed in the same request and mods are not given, mods will be reset.
        /// </summary>
        [JsonPropertyName("required_mods")]
        public IEnumerable<Mod>? RequiredMods { get; set; }

        /// <summary>
        /// The new allowed mods of the playlist item.
        /// Pass <see langword="null"/> to keep the previous ones.
        /// If <see cref="RulesetId"/> is also changed in the same request and mods are not given, mods will be reset.
        /// </summary>
        [JsonPropertyName("allowed_mods")]
        public IEnumerable<Mod>? AllowedMods { get; set; }

        /// <summary>
        /// The new freestyle state of the playlist item.
        /// Pass <see langword="null"/> to keep the previous one.
        /// </summary>
        [JsonPropertyName("freestyle")]
        public bool? Freestyle { get; set; }
    }
}
