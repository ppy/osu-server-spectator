// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Online.Metadata;
using osu.Game.Users;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Metadata;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class MetadataHubTest
    {
        private const int user_id = 55;

        private readonly MetadataHub hub;
        private readonly EntityStore<MetadataClientState> userStates;
        private readonly Mock<IMetadataClient> mockCaller;
        private readonly Mock<IMetadataClient> mockWatchersGroup;
        private readonly Mock<IGroupManager> mockGroupManager;

        public MetadataHubTest()
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            userStates = new EntityStore<MetadataClientState>();

            var mockDatabase = new Mock<IDatabaseAccess>();
            var databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);

            hub = new MetadataHub(cache, userStates, databaseFactory.Object, new Mock<IDailyChallengeUpdater>().Object);

            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(ctx => ctx.UserIdentifier).Returns(user_id.ToString());

            mockWatchersGroup = new Mock<IMetadataClient>();
            mockCaller = new Mock<IMetadataClient>();

            var mockClients = new Mock<IHubCallerClients<IMetadataClient>>();
            mockClients.Setup(clients => clients.Group(MetadataHub.ONLINE_PRESENCE_WATCHERS_GROUP))
                       .Returns(mockWatchersGroup.Object);
            mockClients.Setup(clients => clients.Caller)
                       .Returns(mockCaller.Object);

            mockGroupManager = new Mock<IGroupManager>();

            // this is to ensure that the `Context.GetHttpContext()` call in `MetadataHub.OnConnectedAsync()` doesn't nullref
            // (the method in question is an extension, and it accesses `Features`; mocking further is not required).
            mockContext.Setup(ctx => ctx.Features).Returns(new Mock<IFeatureCollection>().Object);

            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;
            hub.Groups = mockGroupManager.Object;
        }

        [Fact]
        public async Task UserStatusIsTrackedAndCleanedUp()
        {
            await hub.OnConnectedAsync();

            using (var usage = await userStates.GetForUse(user_id))
                Assert.NotNull(usage.Item);

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);

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
        }

        [Fact]
        public async Task OfflineUserUpdatesAreNotBroadcast()
        {
            await hub.OnConnectedAsync();

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, It.IsAny<UserPresence>()), Times.Once);

            await hub.UpdateStatus(UserStatus.Offline);

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserStatus);
                Assert.Equal(UserStatus.Offline, usage.Item!.UserStatus);
            }

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Once);

            await hub.UpdateActivity(new UserActivity.ChoosingBeatmap());

            using (var usage = await userStates.GetForUse(user_id))
            {
                Assert.NotNull(usage.Item!.UserActivity);
                Assert.IsType<UserActivity.ChoosingBeatmap>(usage.Item!.UserActivity);
            }

            mockWatchersGroup.Verify(client => client.UserPresenceUpdated(user_id, null), Times.Exactly(2));
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
    }
}
