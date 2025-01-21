// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using osu.Game.Online.Metadata;
using osu.Game.Users;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Metadata;
using osu.Server.Spectator.Hubs.Spectator;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class MetadataHubTest
    {
        private const int user_id = 55;

        private readonly MetadataHub hub;
        private readonly EntityStore<MetadataClientState> userStates;
        private readonly Mock<HubCallerContext> mockUserContext;
        private readonly Mock<IMetadataClient> mockCaller;
        private readonly Mock<IMetadataClient> mockWatchersGroup;
        private readonly Mock<IGroupManager> mockGroupManager;
        private readonly Mock<IDatabaseAccess> mockDatabase;
        private readonly Mock<IHubCallerClients<IMetadataClient>> mockClients;

        public MetadataHubTest()
        {
            userStates = new EntityStore<MetadataClientState>();

            mockDatabase = new Mock<IDatabaseAccess>();
            var databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);
            var loggerFactoryMock = new Mock<ILoggerFactory>();
            loggerFactoryMock.Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                             .Returns(new Mock<ILogger>().Object);

            hub = new MetadataHub(
                loggerFactoryMock.Object,
                new MemoryCache(new MemoryCacheOptions()),
                userStates,
                databaseFactory.Object,
                new Mock<IDailyChallengeUpdater>().Object,
                new Mock<IScoreProcessedSubscriber>().Object);

            mockWatchersGroup = new Mock<IMetadataClient>();
            mockCaller = new Mock<IMetadataClient>();
            mockGroupManager = new Mock<IGroupManager>();

            mockClients = new Mock<IHubCallerClients<IMetadataClient>>();
            mockClients.Setup(clients => clients.Group(It.IsAny<string>()))
                       .Returns(new Mock<IMetadataClient>().Object);
            mockClients.Setup(clients => clients.Group(MetadataHub.ONLINE_PRESENCE_WATCHERS_GROUP))
                       .Returns(mockWatchersGroup.Object);
            mockClients.Setup(clients => clients.Caller)
                       .Returns(mockCaller.Object);

            mockUserContext = createUserContext(user_id);

            hub.Context = mockUserContext.Object;
            hub.Clients = mockClients.Object;
            hub.Groups = mockGroupManager.Object;
        }

        [Fact]
        public async Task UserStatusIsTrackedAndCleanedUp()
        {
            await hub.OnConnectedAsync();

            using (var usage = await userStates.GetForUse(user_id))
                Assert.NotNull(usage.Item);

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);

            await hub.UpdateStatus(UserStatus.Online);
            await hub.UpdateActivity(new UserActivity.ChoosingBeatmap());

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserActivity);
                Assert.IsType<UserActivity.ChoosingBeatmap>(usage.Item!.UserActivity);
            }

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Exactly(2));

            await hub.UpdateStatus(UserStatus.DoNotDisturb);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserStatus);
                Assert.Equal(UserStatus.DoNotDisturb, usage.Item!.UserStatus);
            }

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Exactly(3));

            await hub.OnDisconnectedAsync(null);

            using (var usage = await userStates.GetForUse(user_id, true))
                Assert.Null(usage.Item);

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Once);
        }

        [Fact]
        public async Task UserSwitchingToAppearOfflineHandledCorrectly()
        {
            await hub.OnConnectedAsync();
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Never);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);

            await hub.UpdateStatus(UserStatus.Online);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Never);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);

            await hub.UpdateStatus(UserStatus.Offline);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Once);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserStatus);
                Assert.Equal(UserStatus.Offline, usage.Item!.UserStatus);
            }

            await hub.UpdateActivity(new UserActivity.ChoosingBeatmap());
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Once);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserActivity);
                Assert.IsType<UserActivity.ChoosingBeatmap>(usage.Item!.UserActivity);
            }

            await hub.OnDisconnectedAsync(null);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Once);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);
        }

        [Fact]
        public async Task AppearOfflineUserUpdatesAreNeverBroadcast()
        {
            await hub.OnConnectedAsync();
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);

            await hub.UpdateStatus(UserStatus.Offline);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserStatus);
                Assert.Equal(UserStatus.Offline, usage.Item!.UserStatus);
            }

            await hub.UpdateActivity(new UserActivity.ChoosingBeatmap());
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserActivity);
                Assert.IsType<UserActivity.ChoosingBeatmap>(usage.Item!.UserActivity);
            }

            await hub.OnDisconnectedAsync(null);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Never);
        }

        [Fact]
        public async Task FreshUserClientOnlyTriggersSinglePresence()
        {
            await hub.OnConnectedAsync();
            await hub.UpdateStatus(UserStatus.DoNotDisturb);
            await hub.UpdateActivity(null);

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Never);
            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.Is<UserPresence>(p => p.Status == UserStatus.DoNotDisturb)), Times.Exactly(1));
        }

        [Fact]
        public async Task UserLoginLogging()
        {
            await hub.OnConnectedAsync();

            // verify that the caller got the initial data update.
            mockDatabase.Verify(caller => caller.AddLoginForUserAsync(user_id, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task UserWatchingHandling()
        {
            using (var usage = await userStates.GetForUse(100, true))
            {
                usage.Item = new MetadataClientState("abcdef", 100, null)
                {
                    UserActivity = new UserActivity.ChoosingBeatmap(),
                    UserStatus = UserStatus.Online
                };
            }

            using (var usage = await userStates.GetForUse(101, true))
            {
                usage.Item = new MetadataClientState("abcdef", 101, null)
                {
                    UserActivity = new UserActivity.ChoosingBeatmap(),
                    UserStatus = UserStatus.DoNotDisturb
                };
            }

            await hub.BeginWatchingUserPresence();
            mockGroupManager.Verify(
                mgr => mgr.AddToGroupAsync(It.IsAny<string>(), MetadataHub.ONLINE_PRESENCE_WATCHERS_GROUP, It.IsAny<CancellationToken>()),
                Times.Once);
            // verify that the caller got the initial data update.
            mockCaller.Verify(caller => caller.UserPresenceUpdated(It.IsAny<int>(), It.IsAny<UserPresence>()), Times.Exactly(2));

            await hub.EndWatchingUserPresence();
            mockGroupManager.Verify(
                mgr => mgr.RemoveFromGroupAsync(It.IsAny<string>(), MetadataHub.ONLINE_PRESENCE_WATCHERS_GROUP, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UserFriendsAlwaysNotified()
        {
            const int friend_id = 56;
            const int non_friend_id = 57;

            Mock<HubCallerContext> friendContext = createUserContext(friend_id);
            Mock<HubCallerContext> nonFriendContext = createUserContext(non_friend_id);

            mockDatabase.Setup(d => d.GetUserFriendsAsync(user_id)).ReturnsAsync([friend_id]);
            mockClients.Setup(clients => clients.Group(MetadataHub.FRIEND_PRESENCE_WATCHERS_GROUP(friend_id)))
                       .Returns(() => mockCaller.Object);

            await hub.OnConnectedAsync();

            // Friend connects...
            hub.Context = friendContext.Object;
            await hub.OnConnectedAsync();
            await hub.UpdateStatus(UserStatus.Online);
            mockCaller.Verify(c => c.FriendPresenceUpdated(friend_id, It.Is<UserPresence>(p => p.Status == UserStatus.Online)), Times.Once);

            // Non-friend connects...
            hub.Context = nonFriendContext.Object;
            await hub.OnConnectedAsync();
            await hub.UpdateStatus(UserStatus.Online);
            mockCaller.Verify(c => c.FriendPresenceUpdated(non_friend_id, It.IsAny<UserPresence>()), Times.Never);

            // Friend disconnects...
            hub.Context = friendContext.Object;
            await hub.OnDisconnectedAsync(null);
            mockCaller.Verify(c => c.FriendPresenceUpdated(friend_id, null), Times.Once);

            // Non-friend disconnects...
            hub.Context = nonFriendContext.Object;
            await hub.OnDisconnectedAsync(null);
            mockCaller.Verify(c => c.FriendPresenceUpdated(non_friend_id, It.IsAny<UserPresence>()), Times.Never);
        }

        [Fact]
        public async Task FriendPresenceBroadcastWhenConnected()
        {
            const int friend_id = 56;
            const int non_friend_id = 57;

            Mock<HubCallerContext> friendContext = createUserContext(friend_id);
            Mock<HubCallerContext> nonFriendContext = createUserContext(non_friend_id);

            mockDatabase.Setup(d => d.GetUserFriendsAsync(user_id)).ReturnsAsync([friend_id]);
            mockClients.Setup(clients => clients.Group(MetadataHub.FRIEND_PRESENCE_WATCHERS_GROUP(friend_id)))
                       .Returns(() => mockCaller.Object);

            // Friend connects...
            hub.Context = friendContext.Object;
            await hub.OnConnectedAsync();
            await hub.UpdateStatus(UserStatus.Online);

            // Non-friend connects...
            hub.Context = nonFriendContext.Object;
            await hub.OnConnectedAsync();
            await hub.UpdateStatus(UserStatus.Online);

            // We connect...
            mockCaller.Invocations.Clear();
            hub.Context = mockUserContext.Object;
            await hub.OnConnectedAsync();
            mockCaller.Verify(c => c.FriendPresenceUpdated(friend_id, It.IsAny<UserPresence>()), Times.Once);
            mockCaller.Verify(c => c.FriendPresenceUpdated(non_friend_id, It.IsAny<UserPresence>()), Times.Never);
        }

        private Mock<HubCallerContext> createUserContext(int userId)
        {
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(ctx => ctx.ConnectionId).Returns(userId.ToString());
            mockContext.Setup(ctx => ctx.UserIdentifier).Returns(userId.ToString());
            // this is to ensure that the `Context.GetHttpContext()` call in `MetadataHub.OnConnectedAsync()` doesn't nullref
            // (the method in question is an extension, and it accesses `Features`; mocking further is not required).
            mockContext.Setup(ctx => ctx.Features).Returns(new Mock<IFeatureCollection>().Object);
            return mockContext;
        }
    }
}
