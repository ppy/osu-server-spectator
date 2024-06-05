// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable InconsistentNaming
    [Serializable]
    public class multiplayer_scores_high
    {
        public ulong id { get; set; }
        public ulong? score_id { get; set; }
        public uint user_id { get; set; }
        public ulong playlist_item_id { get; set; }
        public uint total_score { get; set; }
        public float accuracy { get; set; }
        public float? pp { get; set; }
        public uint attempts { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset updated_at { get; set; }
    }
}
