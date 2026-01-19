// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
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
            ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.multiplayerHubContext = multiplayerHubContext;
            logger = loggerFactory.CreateLogger<MultiplayerEventDispatcher>();
        }

        /// <summary>
        /// Subscribes a connection with the given <paramref name="connectionId"/>
        /// to multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task SubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.AddToGroupAsync(connectionId, MultiplayerHub.GetGroupId(roomId));
        }

        /// <summary>
        /// Unsubscribes a connection with the given <paramref name="connectionId"/>
        /// from multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task UnsubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.RemoveFromGroupAsync(connectionId, MultiplayerHub.GetGroupId(roomId));
        }
    }
}
