// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public class osu_build
    {
        public uint build_id { get; set; }
        public string? version { get; set; }
        public byte[]? hash { get; set; }
        public uint users { get; set; }
        public bool allow_bancho { get; set; }
    }
}
