// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

        private readonly Mock<IDatabaseFactory> mockDatabaseFactory;

        protected readonly Mock<IDatabaseAccess> Database;

        protected readonly Mock<IMultiplayerClient> Receiver;
        protected readonly Mock<IMultiplayerClient> GameplayReceiver;

        protected readonly Mock<HubCallerContext> ContextUser;
        protected readonly Mock<HubCallerContext> ContextUser2;

        protected readonly Mock<IHubCallerClients<IMultiplayerClient>> Clients;
        protected readonly Mock<IGroupManager> Groups;
        protected readonly Mock<IMultiplayerClient> Caller;

        protected MultiplayerTest()
        {
            MultiplayerHub.Reset();

            mockDatabaseFactory = new Mock<IDatabaseFactory>();
            Database = new Mock<IDatabaseAccess>();
            setUpMockDatabase();

            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            Hub = new TestMultiplayerHub(cache, mockDatabaseFactory.Object);

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
                    .ReturnsAsync(new multiplayer_room
                    {
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID
                    });
            Database.Setup(db => db.GetRoomAsync(ROOM_ID_2))
                    .ReturnsAsync(new multiplayer_room
                    {
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID_2
                    });

            Database.Setup(db => db.GetCurrentPlaylistItemAsync(It.IsAny<long>()))
                    .ReturnsAsync(new multiplayer_playlist_item
                    {
                        beatmap_id = 1234
                    });
            Database.Setup(db => db.GetBeatmapChecksumAsync(It.IsAny<int>()))
                    .ReturnsAsync("checksum"); // doesn't matter if bogus, just needs to be non-empty.

            Database.Setup(db => db.GetPlaylistItemFromRoomAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Returns<long, long>((roomId, playlistItemId) => Task.FromResult<multiplayer_playlist_item?>(new multiplayer_playlist_item
                    {
                        id = playlistItemId,
                        room_id = roomId,
                        beatmap_id = 1234,
                    }));
        }
    }
}
