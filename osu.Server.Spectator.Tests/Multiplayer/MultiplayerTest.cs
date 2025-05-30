// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using osu.Game.Beatmaps;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Services;

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

        protected readonly Mock<IDatabaseFactory> DatabaseFactory;
        protected readonly Mock<IDatabaseAccess> Database;

        protected readonly Mock<ISharedInterop> LegacyIO;

        /// <summary>
        /// A general non-gameplay receiver for the room with ID <see cref="ROOM_ID"/>.
        /// </summary>
        protected readonly Mock<DelegatingMultiplayerClient> Receiver;

        /// <summary>
        /// A general non-gameplay receiver for the room with ID <see cref="ROOM_ID_2"/>.
        /// </summary>
        protected readonly Mock<DelegatingMultiplayerClient> Receiver2;

        /// <summary>
        /// A receiver specific to the user with ID <see cref="USER_ID"/>.
        /// </summary>
        protected readonly Mock<DelegatingMultiplayerClient> UserReceiver;

        /// <summary>
        /// A receiver specific to the user with ID <see cref="USER_ID_2"/>.
        /// </summary>
        protected readonly Mock<DelegatingMultiplayerClient> User2Receiver;

        /// <summary>
        /// The user with ID <see cref="USER_ID"/>.
        /// </summary>
        protected readonly Mock<HubCallerContext> ContextUser;

        /// <summary>
        /// The user with ID <see cref="USER_ID_2"/>.
        /// </summary>
        protected readonly Mock<HubCallerContext> ContextUser2;

        protected readonly Mock<IHubCallerClients<IMultiplayerClient>> Clients;
        protected readonly Mock<IGroupManager> Groups;
        protected readonly Mock<IMultiplayerClient> Caller;

        private readonly List<multiplayer_playlist_item> playlistItems;
        private readonly Dictionary<string, List<string>> groupMapping;
        private readonly Dictionary<int, DelegatingMultiplayerClient> clientMapping;

        private int currentItemId;

        protected MultiplayerTest()
        {
            currentItemId = 0;
            playlistItems = new List<multiplayer_playlist_item>();
            groupMapping = new Dictionary<string, List<string>>();
            clientMapping = new Dictionary<int, DelegatingMultiplayerClient>();

            DatabaseFactory = new Mock<IDatabaseFactory>();
            Database = new Mock<IDatabaseAccess>();
            setUpMockDatabase();

            Rooms = new EntityStore<ServerMultiplayerRoom>();
            UserStates = new EntityStore<MultiplayerClientState>();
            Clients = new Mock<IHubCallerClients<IMultiplayerClient>>();
            Groups = new Mock<IGroupManager>();

            Receiver = new Mock<DelegatingMultiplayerClient> { CallBase = true };
            Receiver.Setup(c => c.Clients).Returns(getClientsForGroup(ROOM_ID));

            Receiver2 = new Mock<DelegatingMultiplayerClient> { CallBase = true };
            Receiver2.Setup(c => c.Clients).Returns(getClientsForGroup(ROOM_ID_2));

            Caller = new Mock<IMultiplayerClient>();

            var hubContext = new Mock<IHubContext<MultiplayerHub>>();
            hubContext.Setup(ctx => ctx.Groups).Returns(Groups.Object);
            hubContext.Setup(ctx => ctx.Clients.Client(It.IsAny<string>())).Returns<string>(connectionId => (ISingleClientProxy)Clients.Object.Client(connectionId));
            hubContext.Setup(ctx => ctx.Clients.Group(It.IsAny<string>())).Returns<string>(groupName => (ISingleClientProxy)Clients.Object.Group(groupName));
            hubContext.Setup(ctx => ctx.Clients.All).Returns((ISingleClientProxy)Clients.Object.All);

            Groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
                  {
                      if (!groupMapping.TryGetValue(groupId, out var connectionIds))
                          groupMapping[groupId] = connectionIds = new List<string>();
                      connectionIds.Add(connectionId);
                  });
            Groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
                  {
                      if (!groupMapping.TryGetValue(groupId, out var connectionIds))
                          groupMapping[groupId] = connectionIds = new List<string>();
                      connectionIds.Remove(connectionId);
                  });

            Clients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(ROOM_ID))).Returns(Receiver.Object);
            Clients.Setup(clients => clients.Group(MultiplayerHub.GetGroupId(ROOM_ID_2))).Returns(Receiver2.Object);
            Clients.Setup(client => client.Caller).Returns(Caller.Object);

            var loggerFactoryMock = new Mock<ILoggerFactory>();
            loggerFactoryMock.Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                             .Returns(new Mock<ILogger>().Object);

            LegacyIO = new Mock<ISharedInterop>();
            LegacyIO.Setup(io => io.CreateRoomAsync(It.IsAny<int>(), It.IsAny<MultiplayerRoom>()))
                    .Returns<int, MultiplayerRoom>((_, room) => Task.FromResult(room.RoomID));

            Hub = new TestMultiplayerHub(
                loggerFactoryMock.Object,
                Rooms,
                UserStates,
                DatabaseFactory.Object,
                new ChatFilters(DatabaseFactory.Object),
                hubContext.Object,
                LegacyIO.Object,
                new MultiplayerEventLogger(loggerFactoryMock.Object, DatabaseFactory.Object));
            Hub.Groups = Groups.Object;
            Hub.Clients = Clients.Object;

            CreateUser(USER_ID, out ContextUser, out UserReceiver);
            CreateUser(USER_ID_2, out ContextUser2, out User2Receiver);

            SetUserContext(ContextUser);

            IEnumerable<IMultiplayerClient> getClientsForGroup(long roomId)
            {
                if (!groupMapping.TryGetValue(MultiplayerHub.GetGroupId(roomId), out var connectionIds))
                    yield break;

                foreach (var id in connectionIds)
                    yield return clientMapping[int.Parse(id)];
            }
        }

        protected void CreateUser(int userId, out Mock<HubCallerContext> context, out Mock<DelegatingMultiplayerClient> client)
        {
            context = new Mock<HubCallerContext>();
            context.Setup(context => context.UserIdentifier).Returns(userId.ToString());
            context.Setup(context => context.ConnectionId).Returns(userId.ToString());

            client = new Mock<DelegatingMultiplayerClient>();
            clientMapping[userId] = client.Object;

            Clients.Setup(clients => clients.Client(userId.ToString())).Returns(client.Object);
            Clients.Setup(clients => clients.User(userId.ToString())).Returns(client.Object);
        }

        /// <summary>
        /// Sets the multiplayer hub's current user context.
        /// </summary>
        /// <param name="context">The user context.</param>
        protected void SetUserContext(Mock<HubCallerContext> context) => Hub.Context = context.Object;

        protected async Task MarkCurrentUserReadyAndAvailable()
        {
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
        }

        protected async Task LoadAndFinishGameplay(params Mock<HubCallerContext>[] users)
        {
            await LoadGameplay(users);
            await FinishGameplay(users);
        }

        protected async Task LoadGameplay(params Mock<HubCallerContext>[] users)
        {
            foreach (var u in users)
            {
                SetUserContext(u);

                await Hub.ChangeState(MultiplayerUserState.Loaded);
                await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
            }
        }

        protected async Task FinishGameplay(params Mock<HubCallerContext>[] users)
        {
            foreach (var u in users)
            {
                SetUserContext(u);
                await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            }
        }

        private void setUpMockDatabase()
        {
            DatabaseFactory.Setup(factory => factory.GetInstance()).Returns(Database.Object);

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!),
                    });

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID_2))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!)
                    });

            Database.Setup(db => db.GetBeatmapAsync(It.IsAny<int>()))
                    .ReturnsAsync(new database_beatmap { approved = BeatmapOnlineStatus.Ranked, checksum = "checksum" }); // doesn't matter if bogus, just needs to be non-empty.

            Database.Setup(db => db.GetPlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Returns<long, long>((roomId, playlistItemId) => Task.FromResult(playlistItems.Single(i => i.id == playlistItemId && i.room_id == roomId).Clone()));

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

            Database.Setup(db => db.MarkPlaylistItemAsPlayedAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Callback<long, long>((roomId, playlistItemId) =>
                    {
                        int index = playlistItems.FindIndex(i => i.id == playlistItemId && i.room_id == roomId);
                        var copy = playlistItems[index].Clone();
                        copy.expired = true;
                        copy.played_at = DateTimeOffset.Now;
                        playlistItems[index] = copy;
                    });

            Database.Setup(db => db.GetAllPlaylistItemsAsync(It.IsAny<long>()))
                    .Returns<long>(roomId => Task.FromResult(playlistItems.Where(i => i.room_id == roomId).Select(i => i.Clone()).ToArray()));

            Database.Setup(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Callback<long, long>((roomId, playlistItemId) => playlistItems.RemoveAll(i => i.room_id == roomId && i.id == playlistItemId));
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
                    owner_id = int.Parse(Hub.Context.UserIdentifier!)
                });
            }
        }
    }
}
