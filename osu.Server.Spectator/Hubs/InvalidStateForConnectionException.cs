// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs
{
    public class InvalidStateForConnectionException : InvalidOperationException
    {
        public InvalidStateForConnectionException()
            : base("State is not valid for this connection")
        {
        }

        public InvalidStateForConnectionException(string context)
            : base($"State is not valid for this connection, context: {context}")
        {
        }
    }
}
