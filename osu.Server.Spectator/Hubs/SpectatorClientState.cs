// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs
{
    [Serializable]
    public class SpectatorClientState : ClientState
    {
        public SpectatorState? State;

        [JsonConstructor]
        public SpectatorClientState(in string connectionId, in int userId)
            : base(connectionId, userId)
        {
        }
    }
}
