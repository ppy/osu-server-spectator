// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Entities
{
    /// <summary>
    /// A singleton holding the required entity stores for <see cref="SpectatorHub"/>.
    /// </summary>
    public class SpectatorHubEntities : UserHubEntities<SpectatorClientState>
    {
    }
}
