// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs.Referee
{
    public interface IRefereeHubClient
    {
        Task Pong(string message);
    }
}
