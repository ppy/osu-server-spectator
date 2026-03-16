// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming (matches database table)

using System;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    public enum matchmaking_room_result
    {
        win,
        loss,
        draw
    }
}
