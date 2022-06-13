// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;

namespace osu.Server.Spectator;

public class ServerShuttingDownException : HubException
{
    public ServerShuttingDownException()
        : base("Server is shutting down.")
    {
    }
}
