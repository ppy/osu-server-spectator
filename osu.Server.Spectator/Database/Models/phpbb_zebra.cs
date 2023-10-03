// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class phpbb_zebra
    {
        public int user_id { get; set; }
        public int zebra_id { get; set; }
        public bool friend { get; set; }
        public bool foe { get; set; }
    }
}
