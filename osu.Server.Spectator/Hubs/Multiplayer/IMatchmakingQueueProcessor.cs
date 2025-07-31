// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public interface IMatchmakingQueueProcessor : IHostedService
    {
        Task AddToQueueAsync(string connectionId);
        Task RemoveFromQueueAsync(string connectionId);
    }

    public class MatchmakingQueueProcessor : BackgroundService, IMatchmakingQueueProcessor
    {
        private const int matchmaking_room_size = 8;

        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;

        private readonly object queueLock = new object();
        private readonly HashSet<string> queue = new HashSet<string>();

        public MatchmakingQueueProcessor(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
        }

        public async Task AddToQueueAsync(string connectionId)
        {
            await hub.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.InQueue
            {
                RoomSize = matchmaking_room_size,
                PlayerCount = 1
            });

            lock (queueLock)
                queue.Add(connectionId);
        }

        public async Task RemoveFromQueueAsync(string connectionId)
        {
            bool wasRemoved;

            lock (queueLock)
                wasRemoved = queue.Remove(connectionId);

            if (wasRemoved)
                await hub.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), null);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (true)
                {
                    List<string> ids = new List<string>();

                    // Todo: This should be doing an incremental outwards search for each player.
                    lock (queueLock)
                    {
                        if (queue.Count == 0)
                            break;

                        while (queue.Count > 0)
                        {
                            string connection = queue.First();
                            ids.Add(connection);
                            queue.Remove(connection);
                        }
                    }

                    // Todo: User ID 157 is wrong (should be BanchoBot).
                    long roomId = await sharedInterop.CreateRoomAsync(157, new MultiplayerRoom(0)
                    {
                        Playlist =
                        [
                            // Todo: This is just a dummy playlist item, that has to exist for a bunch of components to behave.
                            //       Its owner should also be BanchoBot (i.e. only the server shall ever change it).
                            new MultiplayerPlaylistItem
                            {
                                BeatmapChecksum = "821afc7f47448c51edf71d004fbb3e23",
                                BeatmapID = 830459,
                            }
                        ]
                    }).ConfigureAwait(false);

                    await hub.Clients.Clients(ids).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.FoundMatch
                    {
                        RoomId = roomId
                    }, cancellationToken: stoppingToken).ConfigureAwait(false);
                }

                await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
