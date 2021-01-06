// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class StatefulUserHubTests
    {
        private readonly TestStatefulHub hub;

        private const int user_id = 1234;

        private readonly Mock<HubCallerContext> mockContext;

        public StatefulUserHubTests()
        {
            TestStatefulHub.Reset();

            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            hub = new TestStatefulHub(cache);

            mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(context => context.UserIdentifier).Returns(user_id.ToString());

            setNewConnectionId();

            hub.Context = mockContext.Object;
        }

        [Fact]
        public async Task ConnectDisconnectStateCleanup()
        {
            await hub.OnConnectedAsync();

            await hub.CreateUserState();

            await hub.OnDisconnectedAsync(null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task SameUserConnectsTwiceDestroysPreviousState()
        {
            await hub.OnConnectedAsync();
            await hub.CreateUserState();

            using (var state = await hub.UserStore.GetForUse(user_id))
            {
                ClientState? firstState = state.Item;

                Assert.NotNull(firstState);
                Assert.Equal(mockContext.Object.ConnectionId, firstState?.ConnectionId);
            }

            // connect a second time as the same user without disconnecting the original connection.
            setNewConnectionId();
            await hub.OnConnectedAsync();

            // original state should have been destroyed.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));
        }

        [Fact]
        public async Task SameUserOldConnectionDoesntDestroyNewState()
        {
            await hub.OnConnectedAsync();
            await hub.CreateUserState();

            string originalConnectionId = mockContext.Object.ConnectionId;

            // connect a second time as the same user without disconnecting the original connection.
            setNewConnectionId();
            await hub.OnConnectedAsync();

            // original state should have been destroyed.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.UserStore.GetForUse(user_id));

            // create a state using the second connection.
            await hub.CreateUserState();
            string lastConnectedConnectionId = mockContext.Object.ConnectionId;

            // ensure disconnecting the original connection does nothing.
            setNewConnectionId(originalConnectionId);
            await hub.OnDisconnectedAsync(null);

            using (var state = await hub.UserStore.GetForUse(user_id))
                Assert.Equal(lastConnectedConnectionId, state.Item?.ConnectionId);
        }

        private void setNewConnectionId(string? connectionId = null) =>
            mockContext.Setup(context => context.ConnectionId).Returns(connectionId ?? Guid.NewGuid().ToString());

        private class TestStatefulHub : StatefulUserHub<object, ClientState>
        {
            public EntityStore<ClientState> UserStore => ACTIVE_STATES;

            public TestStatefulHub([NotNull] IDistributedCache cache)
                : base(cache)
            {
            }

            public async Task CreateUserState()
            {
                using (var state = await GetOrCreateLocalUserState())
                    state.Item = new ClientState(Context.ConnectionId, CurrentContextUserId);
            }
        }
    }
}
