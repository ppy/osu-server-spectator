// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

// ReSharper disable InconsistentNaming (matches database table)

namespace osu.Server.Spectator.Database.Models;

[Serializable]
public class bss_process_queue_item
{
    public int queue_id;
    public int beatmapset_id;
}
