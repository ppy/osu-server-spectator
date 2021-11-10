// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    /// <summary>
    /// Base class for multiplayer unit tests, providing required setup for testing.
    /// </summary>
    public abstract class MultiplayerTest
    {
        protected const int USER_ID = 1234;
        protected const int USER_ID_2 = 2345;

        protected const long ROOM_ID = 8888;
        protected const long ROOM_ID_2 = 9999;

        protected TestMultiplayerHub Hub { get; }
        protected EntityStore<ServerMultiplayerRoom> Rooms { get; }
        protected EntityStore<MultiplayerClientState> UserStates { get; }

        private readonly Mock<IDatabaseFactory> mockDatabaseFactory;

        protected readonly Mock<IDatabaseAccess> Database;

        protected readonly Mock<IMultiplayerClient> Receiver;
        protected readonly Mock<IMultiplayerClient> GameplayReceiver;

        protected readonly Mock<HubCallerContext> ContextUser;
        protected readonly Mock<HubCallerContext> ContextUser2;

        protected readonly Mock<IHubCallerClients<IMultiplayerClient>> Clients;
        protected readonly Mock<IGroupManager> Groups;
        protected readonly Mock<IMultiplayerClient> Caller;

        private readonly List<multiplayer_playlist_item> playlistItems;
        private int currentItemId;

        protected MultiplayerTest()
        {
            currentItemId = 0;
            playlistItems = new List<multiplayer_playlist_item>();

            mockDatabaseFactory = new Mock<IDatabaseFactory>();
            Database = new Mock<IDatabaseAccess>();
            setUpMockDatabase();

            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            Rooms = new EntityStore<ServerMultiplayerRoom>();
            UserStates = new EntityStore<MultiplayerClientState>();
            Hub = new TestMultiplayerHub(cache, Rooms, UserStates, mockDatabaseFactory.Object);

            Clients = new Mock<IHubCallerClients<IMultiplayerClient>>();
            Groups = new Mock<IGroupManager>();

            ContextUser = new Mock<HubCallerContext>();
            ContextUser.Setup(context => context.UserIdentifier).Returns(USER_ID.ToString());
            ContextUser.Setup(context => context.ConnectionId).Returns(USER_ID.ToString());

            ContextUser2 = new Mock<HubCallerContext>();
            ContextUser2.Setup(context => context.UserIdentifier).Returns(USER_ID_2.ToString());
            ContextUser2.Setup(context => context.ConnectionId).Returns(USER_ID_2.ToString());

            Receiver = new Mock<IMultiplayerClient>();
            Clients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(ROOM_ID, false))).Returns(Receiver.Object);

            GameplayReceiver = new Mock<IMultiplayerClient>();
            Clients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(ROOM_ID, true))).Returns(GameplayReceiver.Object);

            var receiver2 = new Mock<IMultiplayerClient>();
            Clients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(ROOM_ID_2, false))).Returns(receiver2.Object);

            Caller = new Mock<IMultiplayerClient>();
            Clients.Setup(client => client.Caller).Returns(Caller.Object);

            Hub.Groups = Groups.Object;
            Hub.Clients = Clients.Object;

            SetUserContext(ContextUser);
        }

        /// <summary>
        /// Verifies that the given user context was either added or not added to the gameplay group.
        /// </summary>
        /// <param name="context">The user context.</param>
        /// <param name="roomId">The room ID.</param>
        /// <param name="wasAdded">Whether to verify that the user context was added, otherwise verify not.</param>
        protected void VerifyAddedToGameplayGroup(Mock<HubCallerContext> context, long roomId, bool wasAdded = true)
            => Groups.Verify(groups => groups.AddToGroupAsync(
                context.Object.ConnectionId,
                MultiplayerHub.GetGroupId(roomId, true),
                It.IsAny<CancellationToken>()), wasAdded ? Times.Once : Times.Never);

        /// <summary>
        /// Verifies that the given user context was either removed or not removed from the gameplay group.
        /// </summary>
        /// <param name="context">The user context.</param>
        /// <param name="roomId">The room ID.</param>
        /// <param name="wasRemoved">Whether to verify that the user context was removed, otherwise verify not.</param>
        protected void VerifyRemovedFromGameplayGroup(Mock<HubCallerContext> context, long roomId, bool wasRemoved = true)
            => Groups.Verify(groups => groups.RemoveFromGroupAsync(
                context.Object.ConnectionId,
                MultiplayerHub.GetGroupId(roomId, true),
                It.IsAny<CancellationToken>()), wasRemoved ? Times.Once : Times.Never);

        /// <summary>
        /// Sets the multiplayer hub's current user context.
        /// </summary>
        /// <param name="context">The user context.</param>
        protected void SetUserContext(Mock<HubCallerContext> context) => Hub.Context = context.Object;

        private void setUpMockDatabase()
        {
            mockDatabaseFactory.Setup(factory => factory.GetInstance()).Returns(Database.Object);

            Database.Setup(db => db.GetRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID,
                    });

            Database.Setup(db => db.GetRoomAsync(ROOM_ID_2))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID_2
                    });

            Database.Setup(db => db.GetBeatmapChecksumAsync(It.IsAny<int>()))
                    .ReturnsAsync("checksum"); // doesn't matter if bogus, just needs to be non-empty.

            Database.Setup(db => db.GetPlaylistItemFromRoomAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Returns<long, long>((roomId, playlistItemId) => Task.FromResult(playlistItems.SingleOrDefault(i => i.room_id == roomId && i.id == playlistItemId)?.Clone()));

            Database.Setup(db => db.HasPlaylistItems(It.IsAny<long>()))
                    .Returns<long>(roomId => Task.FromResult(playlistItems.Any(i => i.room_id == roomId)));

            Database.Setup(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()))
                    .Callback<multiplayer_playlist_item>(item =>
                    {
                        var copy = item.Clone();
                        copy.id = ++currentItemId;
                        copy.expired = false;
                        playlistItems.Add(copy);
                    })
                    .ReturnsAsync(() => currentItemId);

            Database.Setup(db => db.UpdatePlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()))
                    .Callback<multiplayer_playlist_item>(item =>
                    {
                        int index = playlistItems.FindIndex(i => i.id == item.id);
                        playlistItems[index] = item.Clone();
                    });

            Database.Setup(db => db.GetCandidatePlaylistItemByExpiry(It.IsAny<long>()))
                    .Returns<long>(roomId =>
                    {
                        var roomItems = playlistItems.Where(i => i.room_id == roomId);
                        return Task.FromResult((roomItems.FirstOrDefault(i => !i.expired) ?? roomItems.Last()).Clone());
                    });

            Database.Setup(db => db.ExpirePlaylistItemAsync(It.IsAny<long>()))
                    .Callback<long>(playlistItemId =>
                    {
                        int index = playlistItems.FindIndex(i => i.id == playlistItemId);
                        var copy = playlistItems[index].Clone();
                        copy.expired = true;
                        playlistItems[index] = copy;
                    });
        }

        protected void InitialiseRoom(long roomId)
        {
            if (playlistItems.All(i => i.room_id != roomId))
            {
                playlistItems.Add(new multiplayer_playlist_item
                {
                    id = ++currentItemId,
                    room_id = roomId,
                    beatmap_id = 1234,
                });
            }
        }
    }
}
