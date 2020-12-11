// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.DatabaseModels
{
    public class multiplayer_playlist_item
    {
        public long id { get; set; }
        public long room_id { get; set; }
        public int beatmap_id { get; set; }
        public short? ruleset_id { get; set; }
        public short? playlist_order { get; set; }
        public string? allowed_mods { get; set; }
        public string? required_mods { get; set; }
        public DateTimeOffset? created_at { get; set; }
        public DateTimeOffset? updated_at { get; set; }
    }
}
