// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    [Serializable]
    public class MultiplayerClientState : ClientState
    {
        public readonly long CurrentRoomID;

        [JsonConstructor]
        public MultiplayerClientState(in string connectionId, in int userId, in long currentRoomID)
            : base(connectionId, userId)
        {
            CurrentRoomID = currentRoomID;
        }
    }
}
