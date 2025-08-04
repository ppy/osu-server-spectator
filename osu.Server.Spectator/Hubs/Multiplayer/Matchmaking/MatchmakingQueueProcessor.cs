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
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MatchmakingQueueProcessor : BackgroundService, IMatchmakingQueueProcessor
    {
        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;
        private readonly IDatabaseFactory databaseFactory;

        private readonly object queueLock = new object();
        private readonly HashSet<string> queue = new HashSet<string>();

        public MatchmakingQueueProcessor(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop, IDatabaseFactory databaseFactory)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
            this.databaseFactory = databaseFactory;
        }

        public async Task AddToQueueAsync(string connectionId)
        {
            await hub.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.InQueue
            {
                RoomSize = MatchmakingImplementation.MATCHMAKING_ROOM_SIZE,
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
                        if (queue.Count < MatchmakingImplementation.MATCHMAKING_ROOM_SIZE)
                            break;

                        for (int i = 0; i < MatchmakingImplementation.MATCHMAKING_ROOM_SIZE; i++)
                        {
                            string connection = queue.First();
                            ids.Add(connection);
                            queue.Remove(connection);
                        }
                    }

                    MultiplayerRoom room = new MultiplayerRoom(0)
                    {
                        Settings = { MatchType = MatchType.Matchmaking, }
                    };

                    using (var db = databaseFactory.GetInstance())
                    {
                        foreach (int beatmapId in MatchmakingImplementation.BEATMAP_IDS)
                        {
                            var beatmap = (await db.GetBeatmapAsync(beatmapId))!;

                            // Todo: These playlist items should be owned by BanchoBot.
                            room.Playlist.Add(new MultiplayerPlaylistItem
                            {
                                BeatmapID = beatmapId,
                                BeatmapChecksum = beatmap.checksum!,
                                StarRating = beatmap.difficultyrating
                            });
                        }
                    }

                    // Todo: User ID 157 is wrong (should be BanchoBot).
                    long roomId = await sharedInterop.CreateRoomAsync(157, room);

                    await hub.Clients.Clients(ids).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.FoundMatch
                    {
                        RoomId = roomId
                    }, cancellationToken: stoppingToken);
                }

                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
