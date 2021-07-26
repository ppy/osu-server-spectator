// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHubEntities : UserHubEntities<MultiplayerClientState>
    {
        public readonly EntityStore<MultiplayerRoom> ActiveRooms = new EntityStore<MultiplayerRoom>();
    }
}
