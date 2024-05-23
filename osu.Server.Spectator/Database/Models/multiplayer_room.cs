// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class multiplayer_room
    {
        public long id { get; set; }
        public int user_id { get; set; }
        public string name { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public int channel_id { get; set; }
        public DateTimeOffset starts_at { get; set; }
        public DateTimeOffset? ends_at { get; set; }
        public byte max_attempts { get; set; }
        public int participant_count { get; set; }
        public DateTimeOffset? created_at { get; set; }
        public DateTimeOffset? updated_at { get; set; }
        public DateTimeOffset? deleted_at { get; set; }
        public room_category category { get; set; }
        public database_match_type type { get; set; }
        public database_queue_mode queue_mode { get; set; }
        public ushort auto_start_duration { get; set; }
        public bool auto_skip { get; set; }
    }
}
