// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable InconsistentNaming
    [Serializable]
    public class playlist_best_score
    {
        public uint user_id { get; set; }
        public ulong? score_id { get; set; }
        public long room_id { get; set; }
        public ulong playlist_id { get; set; }
        public uint total_score { get; set; }
        public uint attempts { get; set; }
    }
}