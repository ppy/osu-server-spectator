// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class UserAlreadyInRoomException : InvalidOperationException
    {
        public UserAlreadyInRoomException(int userId, long roomId)
            : base($"User {userId} attempted to join room {roomId} they are already present in.")
        {
        }
    }
}
