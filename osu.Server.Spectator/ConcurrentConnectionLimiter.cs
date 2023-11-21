// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using osu.Framework.Extensions.TypeExtensions;
using osu.Framework.Logging;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    public class ConcurrentConnectionLimiter : IHubFilter
    {
        private readonly EntityStore<ConnectionState> connectionStates;

        private readonly IServiceProvider serviceProvider;

        private static readonly IEnumerable<Type> stateful_user_hubs
            = typeof(IStatefulUserHub).Assembly.GetTypes().Where(type => typeof(IStatefulUserHub).IsAssignableFrom(type) && typeof(Hub).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract).ToArray();

        public ConcurrentConnectionLimiter(
            EntityStore<ConnectionState> connectionStates,
            IServiceProvider serviceProvider)
        {
            this.connectionStates = connectionStates;
            this.serviceProvider = serviceProvider;
        }

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            try
            {
                var userId = context.Context.GetUserId();

                using (var userState = await connectionStates.GetForUse(userId, true))
                {
                    if (context.Context.GetTokenId() == userState.Item?.TokenId)
                    {
                        log(context, "subsequent connection from same client instance, registering");
                        userState.Item.RegisterConnectionId(context);
                        return;
                    }

                    if (userState.Item != null)
                    {
                        log(context, "connection from new client instance, dropping existing state");

                        foreach (var hub in stateful_user_hubs)
                        {
                            var hubContextType = typeof(IHubContext<>).MakeGenericType(hub);
                            var hubContext = serviceProvider.GetRequiredService(hubContextType) as IHubContext;
                            hubContext?.Clients.Client(userState.Item.ConnectionIds[hub])
                                      .SendCoreAsync(nameof(IStatefulUserHubClient.DisconnectRequested), Array.Empty<object>());
                        }

                        log(context, "existing state dropped");
                    }
                    else
                        log(context, "connection from first client instance");

                    userState.Item = new ConnectionState(context);
                }
            }
            finally
            {
                await next(context);
            }
        }

        private static void log(HubLifetimeContext context, string message)
            => Logger.Log($"[user:{context.Context.GetUserId()}] [connection:{context.Context.ConnectionId}] [hub:{context.Hub.GetType().ReadableName()}] {message}");

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            // TODO: allow things to execute for hubs that aren't exclusive (like metadata or whatever)

            var userId = invocationContext.Context.GetUserId();

            using (var userState = await connectionStates.GetForUse(userId))
            {
                if (invocationContext.Context.GetTokenId() != userState.Item?.TokenId)
                    throw new InvalidStateException("State is not valid for this connection");
            }

            return await next(invocationContext);
        }

        public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
        {
            try
            {
                if (exception != null)
                    // network disconnection. wait for user to return.
                    return;

                var userId = context.Context.GetUserId();

                using (var userState = await connectionStates.GetForUse(userId, true))
                {
                    if (userState.Item?.TokenId == context.Context.GetTokenId())
                    {
                        log(context, "disconnected");
                        userState.Destroy();
                    }
                }
            }
            finally
            {
                await next(context, exception);
            }
        }
    }
}
