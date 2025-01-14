// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs.Spectator
{
    public class SpectatorList
    {
        public Dictionary<int, SpectatorUser> Spectators { get; } = new Dictionary<int, SpectatorUser>();
    }
}
