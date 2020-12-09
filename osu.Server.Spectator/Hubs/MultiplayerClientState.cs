// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

#nullable enable

namespace osu.Server.Spectator.Hubs
{
    [Serializable]
    public class MultiplayerClientState
    {
        public readonly long CurrentRoomID;

        [JsonConstructor]
        public MultiplayerClientState(in long currentRoomID)
        {
            CurrentRoomID = currentRoomID;
        }
    }
}
