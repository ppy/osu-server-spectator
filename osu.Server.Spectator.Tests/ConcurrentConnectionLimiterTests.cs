// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using osu.Game.Online;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Spectator;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class ConcurrentConnectionLimiterTests
    {
        private readonly EntityStore<ConnectionState> connectionStates;
        private readonly Mock<IServiceProvider> serviceProviderMock;
        private readonly Mock<ILoggerFactory> loggerFactoryMock;
        private readonly Mock<Hub> hubMock;

        public ConcurrentConnectionLimiterTests()
        {
            connectionStates = new EntityStore<ConnectionState>();
            serviceProviderMock = new Mock<IServiceProvider>();

            var hubContextMock = new Mock<IHubContext>();
            serviceProviderMock.Setup(sp => sp.GetService(It.IsAny<Type>()))
                               .Returns(hubContextMock.Object);

            loggerFactoryMock = new Mock<ILoggerFactory>();
            loggerFactoryMock.Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                             .Returns(new Mock<ILogger>().Object);

            hubMock = new Mock<Hub>();
        }

        #region New path (uses client-side generated session GUID)

        [Fact]
        public async Task TestNormalOperation_SessionIDPresent()
        {
            var hubCallerContextMock = new Mock<HubCallerContext>();
            var httpContextMock = new Mock<IHttpContextFeature>();
            hubCallerContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            hubCallerContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));
            hubCallerContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(httpContextMock.Object);
            httpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = Guid.NewGuid().ToString();
                return context;
            });

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);
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
        public async Task TestConcurrencyBlocked_SessionIDPresent()
        {
            var firstHubCallerContext = new Mock<HubCallerContext>();
            var firstHttpContextMock = new Mock<IHttpContextFeature>();
            var secondHubCallerContext = new Mock<HubCallerContext>();
            var secondHttpContextMock = new Mock<IHttpContextFeature>();

            firstHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));
            firstHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(firstHttpContextMock.Object);
            firstHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = Guid.NewGuid().ToString();
                return context;
            });

            secondHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));
            secondHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(secondHttpContextMock.Object);
            secondHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = Guid.NewGuid().ToString();
                return context;
            });

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);

            var secondInvocationContext = new HubInvocationContext(secondHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object,
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            // should succeed.
            await filter.InvokeMethodAsync(secondInvocationContext, _ => new ValueTask<object?>(new object()));

            var firstInvocationContext = new HubInvocationContext(firstHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object,
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            // should throw.
            await Assert.ThrowsAsync<InvalidOperationException>(() => filter.InvokeMethodAsync(firstInvocationContext, _ => new ValueTask<object?>(new object())).AsTask());
        }

        [Fact]
        public async Task TestStaleDisconnectIsANoOp_SessionIDPresent()
        {
            var firstHubCallerContext = new Mock<HubCallerContext>();
            var firstHttpContextMock = new Mock<IHttpContextFeature>();
            var secondHubCallerContext = new Mock<HubCallerContext>();
            var secondHttpContextMock = new Mock<IHttpContextFeature>();
            string commonSessionId = Guid.NewGuid().ToString();

            firstHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonSessionId)
                })
            }));
            firstHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(firstHttpContextMock.Object);
            firstHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            secondHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonSessionId)
                })
            }));
            secondHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(secondHttpContextMock.Object);
            secondHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondHubCallerContext.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);

            await filter.OnDisconnectedAsync(firstLifetimeContext, null, (_, _) => Task.CompletedTask);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds.Single().Value);
        }

        [Fact]
        public async Task TestHubDisconnectsTrackedSeparately_SessionIDPresent()
        {
            var firstHubCallerContext = new Mock<HubCallerContext>();
            var firstHttpContextMock = new Mock<IHttpContextFeature>();
            var secondHubCallerContext = new Mock<HubCallerContext>();
            var secondHttpContextMock = new Mock<IHttpContextFeature>();
            string commonSessionId = Guid.NewGuid().ToString();

            firstHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonSessionId)
                })
            }));
            firstHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(firstHttpContextMock.Object);
            firstHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            secondHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonSessionId)
                })
            }));
            secondHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(secondHttpContextMock.Object);
            secondHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstHubCallerContext.Object, serviceProviderMock.Object, new FirstHub());
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondHubCallerContext.Object, serviceProviderMock.Object, new SecondHub());
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);
            Assert.Equal(2, connectionStates.GetEntityUnsafe(1234)!.ConnectionIds.Count);
            Assert.Equal("abcd", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(FirstHub)]);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(SecondHub)]);

            await filter.OnDisconnectedAsync(firstLifetimeContext, null, (_, _) => Task.CompletedTask);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(SecondHub)]);
        }

        [Fact]
        public async Task TestSessionIDOverrulesTokenID()
        {
            var firstHubCallerContext = new Mock<HubCallerContext>();
            var firstHttpContextMock = new Mock<IHttpContextFeature>();
            var secondHubCallerContext = new Mock<HubCallerContext>();
            var secondHttpContextMock = new Mock<IHttpContextFeature>();
            string commonSessionId = Guid.NewGuid().ToString();

            firstHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", "first token ID")
                })
            }));
            firstHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(firstHttpContextMock.Object);
            firstHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            secondHubCallerContext.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondHubCallerContext.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondHubCallerContext.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", "second token ID")
                })
            }));
            secondHubCallerContext.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(secondHttpContextMock.Object);
            secondHttpContextMock.Setup(ctx => ctx.HttpContext).Returns(() =>
            {
                var context = new DefaultHttpContext();
                context.Request.Headers[HubClientConnector.CLIENT_SESSION_ID_HEADER] = commonSessionId;
                return context;
            });

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstHubCallerContext.Object, serviceProviderMock.Object, new FirstHub());
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondHubCallerContext.Object, serviceProviderMock.Object, new SecondHub());
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);

            var firstInvocationContext = new HubInvocationContext(firstHubCallerContext.Object, serviceProviderMock.Object, new FirstHub(),
                typeof(SpectatorHub).GetMethod(nameof(SpectatorHub.StartWatchingUser))!, new object[] { 1234 });
            // should not throw.
            await filter.InvokeMethodAsync(firstInvocationContext, _ => new ValueTask<object?>(new object()));
        }

        #endregion

        #region Legacy path (uses JWT `jti` claim to distinguish clients)

        [Fact]
        public async Task TestNormalOperation_SessionIDNotPresent()
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
            hubCallerContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);
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
        public async Task TestConcurrencyBlocked_SessionIDNotPresent()
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
            firstContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            secondContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondContextMock.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString())
                })
            }));
            secondContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

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
            await Assert.ThrowsAsync<InvalidOperationException>(() => filter.InvokeMethodAsync(firstInvocationContext, _ => new ValueTask<object?>(new object())).AsTask());
        }

        [Fact]
        public async Task TestStaleDisconnectIsANoOp_SessionIDNotPresent()
        {
            var firstContextMock = new Mock<HubCallerContext>();
            var secondContextMock = new Mock<HubCallerContext>();
            string commonTokenId = Guid.NewGuid().ToString();

            firstContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstContextMock.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonTokenId)
                })
            }));
            firstContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            secondContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondContextMock.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonTokenId)
                })
            }));
            secondContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstContextMock.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondContextMock.Object, serviceProviderMock.Object, hubMock.Object);
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);

            await filter.OnDisconnectedAsync(firstLifetimeContext, null, (_, _) => Task.CompletedTask);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds.Single().Value);
        }

        [Fact]
        public async Task TestHubDisconnectsTrackedSeparately_SessionIDNotPresent()
        {
            var firstContextMock = new Mock<HubCallerContext>();
            var secondContextMock = new Mock<HubCallerContext>();
            string commonTokenId = Guid.NewGuid().ToString();

            firstContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            firstContextMock.Setup(ctx => ctx.ConnectionId).Returns("abcd");
            firstContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonTokenId)
                })
            }));
            firstContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            secondContextMock.Setup(ctx => ctx.UserIdentifier).Returns("1234");
            secondContextMock.Setup(ctx => ctx.ConnectionId).Returns("efgh");
            secondContextMock.Setup(ctx => ctx.User).Returns(new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("jti", commonTokenId)
                })
            }));
            secondContextMock.Setup(ctx => ctx.Features.Get<IHttpContextFeature>()).Returns(new Mock<IHttpContextFeature>().Object);

            var filter = new ConcurrentConnectionLimiter(connectionStates, serviceProviderMock.Object, loggerFactoryMock.Object);

            var firstLifetimeContext = new HubLifetimeContext(firstContextMock.Object, serviceProviderMock.Object, new FirstHub());
            await filter.OnConnectedAsync(firstLifetimeContext, _ => Task.CompletedTask);

            var secondLifetimeContext = new HubLifetimeContext(secondContextMock.Object, serviceProviderMock.Object, new SecondHub());
            await filter.OnConnectedAsync(secondLifetimeContext, _ => Task.CompletedTask);
            Assert.Equal(2, connectionStates.GetEntityUnsafe(1234)!.ConnectionIds.Count);
            Assert.Equal("abcd", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(FirstHub)]);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(SecondHub)]);

            await filter.OnDisconnectedAsync(firstLifetimeContext, null, (_, _) => Task.CompletedTask);
            Assert.Single(connectionStates.GetEntityUnsafe(1234)!.ConnectionIds);
            Assert.Equal("efgh", connectionStates.GetEntityUnsafe(1234)!.ConnectionIds[typeof(SecondHub)]);
        }

        #endregion

        private class FirstHub : Hub
        {
        }

        private class SecondHub : Hub
        {
        }
    }
}
