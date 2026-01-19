// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerEventDispatcher
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MultiplayerHub> multiplayerHubContext;
        private readonly ILogger<MultiplayerEventDispatcher> logger;

        public MultiplayerEventDispatcher(
            IDatabaseFactory databaseFactory,
            IHubContext<MultiplayerHub> multiplayerHubContext,
            ILogger<MultiplayerEventDispatcher> logger)
        {
            this.databaseFactory = databaseFactory;
            this.multiplayerHubContext = multiplayerHubContext;
            this.logger = logger;
        }
    }
}
