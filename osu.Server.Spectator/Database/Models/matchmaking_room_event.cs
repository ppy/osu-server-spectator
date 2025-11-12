// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable InconsistentNaming

    public class matchmaking_room_event
    {
        public long id { get; set; }
        public long room_id { get; set; }
        public string event_type { get; set; } = string.Empty;
        public long? playlist_item_id { get; set; }
        public int? user_id { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset updated_at { get; set; }
        public string? event_detail { get; set; }
    }
}
