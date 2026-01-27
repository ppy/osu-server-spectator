// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Referee
{
    [Authorize(ConfigureJwtBearerOptions.REFEREE_CLIENT_SCHEME)]
    public class RefereeHub : Hub, IRefereeHubServer
    {
        private readonly IDatabaseFactory databaseFactory;

        public RefereeHub(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;
        }

        public async Task Ping(string message)
        {
            string? username;

            using (var db = databaseFactory.GetInstance())
                username = await db.GetUsernameAsync(Context.GetUserId());

            await Clients.Caller.SendAsync(nameof(IRefereeHubClient.Pong), $"Hi {username}! Here's your message back: {message}");
        }
    }
}
