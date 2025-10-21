// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using StatsdClient;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    [Serializable]
    public class MultiplayerClientState : ClientState
    {
        public long? CurrentRoomID { get; private set; }

        [JsonConstructor]
        public MultiplayerClientState(in string connectionId, in int userId)
            : base(connectionId, userId)
        {
        }

        public void SetRoom(long roomId)
        {
            if (CurrentRoomID != null)
                throw new InvalidOperationException("User is already in a room.");

            CurrentRoomID = roomId;
            DogStatsd.Increment($"{MultiplayerHub.STATSD_PREFIX}.users");
        }

        public void ClearRoom()
        {
            if (CurrentRoomID == null)
                throw new InvalidOperationException("User is not in a room.");

            CurrentRoomID = null;
            DogStatsd.Decrement($"{MultiplayerHub.STATSD_PREFIX}.users");
        }
    }
}
