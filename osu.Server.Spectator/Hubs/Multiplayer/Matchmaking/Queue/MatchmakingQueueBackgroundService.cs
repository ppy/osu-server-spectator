// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingQueueBackgroundService : BackgroundService, IMatchmakingQueueBackgroundService
    {
        /// <summary>
        /// The rate at which the matchmaking queue is updated.
        /// </summary>
        private static readonly TimeSpan queue_update_rate = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The rate at which actively searching users are sent periodic status updates.
        /// </summary>
        private static readonly TimeSpan periodic_update_rate = TimeSpan.FromSeconds(5);

        private const string lobby_users_group = "matchmaking-lobby-users";

        private readonly MatchmakingQueue queue = new MatchmakingQueue();

        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;
        private readonly IDatabaseFactory databaseFactory;

        private int[] queuedUsersSample = [];
        private MultiplayerPlaylistItem[]? playlistItems;
        private DateTimeOffset lastLobbyUpdateTime = DateTimeOffset.UnixEpoch;

        public MatchmakingQueueBackgroundService(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop, IDatabaseFactory databaseFactory)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
            this.databaseFactory = databaseFactory;
        }

        public bool IsInQueue(MultiplayerClientState state)
        {
            return queue.IsInQueue(new MatchmakingQueueUser(state.ConnectionId));
        }

        public async Task AddToLobbyAsync(MultiplayerClientState state)
        {
            await hub.Groups.AddToGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task RemoveFromLobbyAsync(MultiplayerClientState state)
        {
            await hub.Groups.RemoveFromGroupAsync(state.ConnectionId, lobby_users_group);
        }

        public async Task AddToQueueAsync(MultiplayerClientState state)
        {
            MatchmakingQueueUser user = new MatchmakingQueueUser(state.ConnectionId)
            {
                UserId = state.UserId
            };

            using (var db = databaseFactory.GetInstance())
                user.Rank = (int)await db.GetUserPP(state.UserId, 0);

            await processBundle(queue.Add(user));
        }

        public async Task RemoveFromQueueAsync(MultiplayerClientState state)
        {
            await processBundle(queue.Remove(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task AcceptInvitationAsync(MultiplayerClientState state)
        {
            // Immediately notify the incoming user of their intent to join the match.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.JoiningMatch());

            await processBundle(queue.MarkInvitationAccepted(new MatchmakingQueueUser(state.ConnectionId)));
        }

        public async Task DeclineInvitationAsync(MultiplayerClientState state)
        {
            await processBundle(queue.MarkInvitationDeclined(new MatchmakingQueueUser(state.ConnectionId)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await updateLobby();
                await processBundle(queue.Update());
                await Task.Delay(queue_update_rate, stoppingToken);
            }
        }

        private async Task updateLobby()
        {
            if (DateTimeOffset.Now - lastLobbyUpdateTime < periodic_update_rate)
                return;

            MatchmakingQueueUser[] users = queue.GetAllUsers();
            Random.Shared.Shuffle(users);
            queuedUsersSample = users.Take(50).Select(u => u.UserId).ToArray();

            await hub.Clients.Group(lobby_users_group).SendAsync(nameof(IMultiplayerClient.MatchmakingLobbyStatusChanged), new MatchmakingLobbyStatus
            {
                UsersInQueue = queuedUsersSample
            });

            lastLobbyUpdateTime = DateTimeOffset.Now;
        }

        private async Task processBundle(MatchmakingQueueUpdateBundle bundle)
        {
            foreach (var user in bundle.RemovedUsers)
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueLeft));

            foreach (var user in bundle.AddedUsers)
            {
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueJoined));
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.Searching());
            }

            foreach (var group in bundle.FormedGroups)
            {
                foreach (var user in group.Users)
                    await hub.Groups.AddToGroupAsync(user.Identifier, group.Identifier, CancellationToken.None);

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingRoomInvited));
                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.MatchFound());
            }

            foreach (var group in bundle.CompletedGroups)
            {
                long roomId = await sharedInterop.CreateRoomAsync(AppSettings.BanchoBotUserId, new MultiplayerRoom(0)
                {
                    Settings = { MatchType = MatchType.Matchmaking },
                    Playlist = await createPlaylistItems()
                });

                await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingRoomReady), roomId);

                foreach (var user in group.Users)
                    await hub.Groups.RemoveFromGroupAsync(user.Identifier, group.Identifier);
            }
        }

        private async Task<MultiplayerPlaylistItem[]> createPlaylistItems()
        {
            if (playlistItems == null)
            {
                using (var db = databaseFactory.GetInstance())
                {
                    database_beatmap[] beatmaps = await db.GetBeatmapsAsync(MatchmakingImplementation.BEATMAP_IDS);
                    playlistItems = beatmaps.Select(b => new MultiplayerPlaylistItem
                    {
                        BeatmapID = b.beatmap_id,
                        BeatmapChecksum = b.checksum!,
                        StarRating = b.difficultyrating
                    }).ToArray();
                }
            }

            // Per-room isolation of playlist items.
            return playlistItems.Select(p => p.Clone()).ToArray();
        }
    }
}
