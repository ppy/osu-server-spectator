// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class chat_filter
    {
        public long id { get; set; }
        public string match { get; set; } = string.Empty;
        public string replacement { get; set; } = string.Empty;
    }
}
