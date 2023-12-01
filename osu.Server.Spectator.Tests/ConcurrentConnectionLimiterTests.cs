// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Spectator;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class ConcurrentConnectionLimiterTests
    {
        private readonly EntityStore<ConnectionState> connectionStates;
        private readonly Mock<IServiceProvider> serviceProviderMock;
        private readonly Mock<Hub> hubMock;

        public ConcurrentConnectionLimiterTests()
        {
            connectionStates = new EntityStore<ConnectionState>();
            serviceProviderMock = new Mock<IServiceProvider>();

            var hubContextMock = new Mock<IHubContext>();
            serviceProviderMock.Setup(sp => sp.GetService(It.IsAny<Type>()))
                               .Returns(hubContextMock.Object);

            hubMock = new Mock<Hub>();
        }

        [Fact]
        public async Task TestNormalOperation()
        {
            var hubCallerContextMock = new Mock<HubCallerContext>();
            hubCallerContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            hubCallerContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object);
            var lifetimeContext = new HubLifetimeContext(hubCallerContextMock.Object, serviceProviderMock.Object, hubMock.Object);

            bool connected = false;
            await filter.OnConnectedAsync(lifetimeContext, _ =>
            {
                connected = true;
                return Task.CompletedTask;
            });
            Assert.True(connected);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);

            bool methodInvoked = false;
            var invocationContext = new HubInvocationContext(hubCallerContextMock.Object, serviceProviderMock.Object, hubMock.Object,
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            await filter.InvokeMethodAsync(invocationContext, _ =>
            {
                methodInvoked = true;
                return new ValueTask<object?>(new object());
            });
            Assert.True(methodInvoked);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);

            bool disconnected = false;
            await filter.OnDisconnectedAsync(lifetimeContext, null, (_, _) =>
            {
                disconnected = true;
                return Task.CompletedTask;
            });
            Assert.True(disconnected);
            Assert.Null(connectionStates.GetEntityUnsafe(1234));
        }

        [Fact]
        public async Task TestConcurrencyBlocked()
        {
            var firstContextMock = new Mock<HubCallerContext>();
            var secondContextMock = new Mock<HubCallerContext>();

            firstContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstContextMock.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));

            secondContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondContextMock.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstContextMock.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondContextMock.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);

            var secondInvocationContext = new HubInvocationContext(secondContextMock.Object, serviceProviderMock.Object, hubMock.Object,
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            // should succeed.
            await filter.InvokeMethodAsync(secondInvocationContext, _ => new ValueTask<object?>(new object()));

            var firstInvocationContext = new HubInvocationContext(firstContextMock.Object, serviceProviderMock.Object, hubMock.Object,
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            // should throw.
            await Assert.ThrowsAsync<InvalidStateException>(() => filter.InvokeMethodAsync(firstInvocationContext, _ => new ValueTask<object?>(new object())).AsTask());
        }
    }
}
