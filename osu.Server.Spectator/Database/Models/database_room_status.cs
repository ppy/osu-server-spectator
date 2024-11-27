// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable once InconsistentNaming
    [Serializable]
    public enum database_room_status
    {
        idle,
        playing
    }

    public static class DatabaseRoomStatusExtensions
    {
        public static database_room_status ToDatabaseRoomStatus(this MultiplayerRoomState state)
        {
            switch (state)
            {
                case MultiplayerRoomState.Open:
                case MultiplayerRoomState.Closed:
                    return database_room_status.idle;

                case MultiplayerRoomState.WaitingForLoad:
                case MultiplayerRoomState.Playing:
                    return database_room_status.playing;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }
    }
}
