// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MatchmakingQueueService : BackgroundService, IMatchmakingQueueService
    {
        /// <summary>
        /// All users active in the matchmaking queue.
        /// </summary>
        private readonly HashSet<MatchmakingUser> matchmakingUsers = new HashSet<MatchmakingUser>();

        private readonly object queueLock = new object();

        private readonly IHubContext<MultiplayerHub> hub;
        private readonly ISharedInterop sharedInterop;
        private readonly IDatabaseFactory databaseFactory;

        private MultiplayerPlaylistItem[]? playlistItems;
        private int matchmakingGroupId = 1;

        public MatchmakingQueueService(IHubContext<MultiplayerHub> hub, ISharedInterop sharedInterop, IDatabaseFactory databaseFactory)
        {
            this.hub = hub;
            this.sharedInterop = sharedInterop;
            this.databaseFactory = databaseFactory;
        }

        public async Task<bool> AddToQueueAsync(MultiplayerClientState state)
        {
            MatchmakingUser user = new MatchmakingUser(state.ConnectionId);

            // Precheck for if we're definitely not going to be adding the user.
            lock (queueLock)
            {
                if (matchmakingUsers.Contains(user))
                    return false;
            }

            using (var db = databaseFactory.GetInstance())
                user.Rank = (int)await db.GetUserPP(state.UserId, 0);

            lock (queueLock)
            {
                if (!matchmakingUsers.Add(user))
                    return false;
            }

            // Notify the incoming user they've been added to the queue.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueJoined));
            await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.Searching());

            return true;
        }

        public async Task RemoveFromQueueAsync(MultiplayerClientState state)
        {
            await DeclineInvitationAsync(state);

            lock (queueLock)
            {
                if (!matchmakingUsers.Remove(new MatchmakingUser(state.ConnectionId)))
                    return;
            }

            // Notify the incoming user they've been removed from the queue.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueLeft));
        }

        public async Task AcceptInvitationAsync(MultiplayerClientState state)
        {
            MatchmakingGroup? finalisedGroup = null;

            lock (queueLock)
            {
                if (!matchmakingUsers.TryGetValue(new MatchmakingUser(state.ConnectionId), out MatchmakingUser? user))
                    return;

                if (user.Group == null)
                    return;

                user.AcceptedInvitation = true;

                if (user.Group.Users.All(u => u.AcceptedInvitation))
                {
                    finalisedGroup = user.Group;

                    foreach (var u in user.Group.Users)
                        matchmakingUsers.Remove(u);
                }
            }

            // Notify the incoming user of their intent to join the match.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.JoiningMatch());

            if (finalisedGroup != null)
            {
                // Notify all users that they've been removed from the queue.
                await hub.Clients.Group(finalisedGroup.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueLeft));

                long roomId = await sharedInterop.CreateRoomAsync(AppSettings.BanchoBotUserId, new MultiplayerRoom(0)
                {
                    Settings = { MatchType = MatchType.Matchmaking },
                    Playlist = await createPlaylistItems()
                });

                // Notify all users that the room is now ready to be joined.
                await hub.Clients.Group(finalisedGroup.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingRoomReady), roomId);

                foreach (var user in finalisedGroup.Users)
                    await hub.Groups.RemoveFromGroupAsync(user.Identifier, finalisedGroup.Identifier);
            }
        }

        public async Task DeclineInvitationAsync(MultiplayerClientState state)
        {
            // Users which have been returned to the queue because a player declined the invitation.
            List<MatchmakingUser> returnedUsers = new List<MatchmakingUser>();

            lock (queueLock)
            {
                if (!matchmakingUsers.TryGetValue(new MatchmakingUser(state.ConnectionId), out MatchmakingUser? user))
                    return;

                if (user.Group == null)
                    return;

                matchmakingUsers.Remove(user);

                foreach (MatchmakingUser u in user.Group.Users)
                {
                    if (u.Equals(user))
                        continue;

                    u.Group = null;
                    u.AcceptedInvitation = false;

                    returnedUsers.Add(u);
                }
            }

            // Notify the incoming user they've been removed from the queue.
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueLeft));

            // Notify all other users they've been returned to the queue.
            foreach (var user in returnedUsers)
            {
                await hub.Clients.Client(user.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.Searching
                {
                    ReturnedToQueue = true
                });
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<MatchmakingGroup> newGroups = new List<MatchmakingGroup>();

                lock (queueLock)
                {
                    foreach (MatchmakingUser[] users in groupAvailableUsers())
                    {
                        if (users.Length < MatchmakingImplementation.MATCHMAKING_ROOM_SIZE)
                            break;

                        MatchmakingGroup group = new MatchmakingGroup($"matchmaking-{matchmakingGroupId++}", users);
                        newGroups.Add(group);

                        foreach (var user in users)
                            user.Group = group;
                    }
                }

                foreach (var group in newGroups)
                {
                    foreach (var user in group.Users)
                        await hub.Groups.AddToGroupAsync(user.Identifier, group.Identifier, CancellationToken.None);

                    // Notify all users that a match has been found.
                    await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingRoomInvited), CancellationToken.None);
                    await hub.Clients.Group(group.Identifier).SendAsync(nameof(IMultiplayerClient.MatchmakingQueueStatusChanged), new MatchmakingQueueStatus.MatchFound(), CancellationToken.None);
                }

                // Todo: We can notify all other users here of their expected time in the queue?

                await Task.Delay(5000, stoppingToken);
            }
        }

        private IEnumerable<MatchmakingUser[]> groupAvailableUsers()
        {
            return matchmakingUsers
                   .Where(u => u.Group == null)
                   .OrderByDescending(u => u.Rank)
                   .Chunk(MatchmakingImplementation.MATCHMAKING_ROOM_SIZE);
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

        /// <summary>
        /// An active matchmaking user.
        /// </summary>
        private class MatchmakingUser : IEquatable<MatchmakingUser>
        {
            /// <summary>
            /// This user's rank.
            /// </summary>
            public int Rank { get; set; }

            /// <summary>
            /// Whether this user has accepted the matchmaking room invitation.
            /// </summary>
            public bool AcceptedInvitation { get; set; }

            /// <summary>
            /// The group which this user has been matched with.
            /// </summary>
            public MatchmakingGroup? Group { get; set; }

            /// <summary>
            /// A unique identifier for this user.
            /// </summary>
            public readonly string Identifier;

            public MatchmakingUser(string identifier)
            {
                Identifier = identifier;
            }

            public bool Equals(MatchmakingUser? other)
                => other != null && Identifier == other.Identifier;

            public override bool Equals(object? obj)
                => obj is MatchmakingUser other && Equals(other);

            public override int GetHashCode()
                => Identifier.GetHashCode();
        }

        /// <summary>
        /// An active matchmaking group.
        /// </summary>
        private class MatchmakingGroup : IEquatable<MatchmakingGroup>
        {
            /// <summary>
            /// A unique identifier for this group.
            /// </summary>
            public readonly string Identifier;

            /// <summary>
            /// The users that are part of this group.
            /// </summary>
            public readonly MatchmakingUser[] Users;

            public MatchmakingGroup(string identifier, MatchmakingUser[] users)
            {
                Identifier = identifier;
                Users = users;
            }

            public bool Equals(MatchmakingGroup? other)
                => other != null && Identifier == other.Identifier;

            public override bool Equals(object? obj)
                => obj is MatchmakingGroup other && Equals(other);

            public override int GetHashCode()
                => Identifier.GetHashCode();
        }
    }
}
