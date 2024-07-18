// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using osu.Framework.Extensions.TypeExtensions;
using osu.Game.Online;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    public class ConcurrentConnectionLimiter : IHubFilter
    {
        private readonly EntityStore<ConnectionState> connectionStates;

        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;

        private static readonly IEnumerable<Type> stateful_user_hubs
            = typeof(IStatefulUserHub).Assembly.GetTypes().Where(type => typeof(IStatefulUserHub).IsAssignableFrom(type) && typeof(Hub).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract).ToArray();

        public ConcurrentConnectionLimiter(
            EntityStore<ConnectionState> connectionStates,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory)
        {
            this.connectionStates = connectionStates;
            this.serviceProvider = serviceProvider;
            logger = loggerFactory.CreateLogger(nameof(ConcurrentConnectionLimiter));
        }

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            await registerConnection(context);
            await next(context);
        }

        private async Task registerConnection(HubLifetimeContext context)
        {
            int userId = context.Context.GetUserId();

            using (var userState = await connectionStates.GetForUse(userId, true))
            {
                if (userState.Item == null)
                {
                    log(context, "connection from first client instance");
                    userState.Item = new ConnectionState(context);
                    return;
                }

                if (userState.Item.IsConnectionFromSameClient(context))
                {
                    // The assumption is that the client has already dropped the old connection,
                    // so we don't bother to ask for a disconnection.

                    log(context, "subsequent connection from same client instance, registering");
                    // Importantly, this will replace the old connection, ensuring it cannot be
                    // used to communicate on anymore.
                    userState.Item.RegisterConnectionId(context);
                    return;
                }

                log(context, "connection from new client instance, dropping existing state");

                foreach (var hubType in stateful_user_hubs)
                {
                    var hubContextType = typeof(IHubContext<>).MakeGenericType(hubType);
                    var hubContext = serviceProvider.GetRequiredService(hubContextType) as IHubContext;

                    if (userState.Item.ConnectionIds.TryGetValue(hubType, out string? connectionId))
                    {
                        hubContext?.Clients.Client(connectionId)
                                  .SendCoreAsync(nameof(IStatefulUserHubClient.DisconnectRequested), Array.Empty<object>());
                    }
                }

                log(context, "existing state dropped");
                userState.Item = new ConnectionState(context);
            }
        }

        private void log(HubLifetimeContext context, string message)
            => logger.LogInformation("[user:{user}] [connection:{connection}] [hub:{hub}] {message}",
                context.Context.GetUserId(),
                context.Context.ConnectionId,
                context.Hub.GetType().ReadableName(),
                message);

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            int userId = invocationContext.Context.GetUserId();

            using (var userState = await connectionStates.GetForUse(userId))
            {
                if (userState.Item?.ExistingConnectionMatches(invocationContext) != true)
                    throw new InvalidOperationException($"State is not valid for this connection, context: {LoggingHubFilter.GetMethodCallDisplayString(invocationContext)})");
            }

            return await next(invocationContext);
        }

        public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
        {
            // if `exception` isn't null then the disconnection is not clean,
            // so don't unregister yet in hopes that the user will return after a transient network failure or similar.
            if (exception == null)
                await unregisterConnection(context, exception);
            await next(context, exception);
        }

        private async Task unregisterConnection(HubLifetimeContext context, Exception? exception)
        {
            int userId = context.Context.GetUserId();

            using (var userState = await connectionStates.GetForUse(userId, true))
            {
                if (userState.Item?.ExistingConnectionMatches(context) == true)
                {
                    log(context, "disconnected from hub");
                    userState.Item!.ConnectionIds.Remove(context.Hub.GetType());
                }

                if (userState.Item?.ConnectionIds.Count == 0)
                {
                    log(context, "all connections closed, destroying state");
                    userState.Destroy();
                }
            }
        }
    }
}
