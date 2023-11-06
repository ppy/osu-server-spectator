// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Logging;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator
{
    public class ConcurrentConnectionLimiter : IHubFilter
    {
        private readonly EntityStore<ConnectionState> connectionStates;

        public ConcurrentConnectionLimiter(
            EntityStore<ConnectionState> connectionStates)
        {
            this.connectionStates = connectionStates;
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
                        Logger.Log($"connection continued for user {userId}");
                        return;
                    }

                    if (userState.Item == null)
                        Logger.Log($"{userId} connected");
                    else
                        Logger.Log($"new connection spotted for user {userId}, should drop existing");

                    userState.Item = new ConnectionState(context.Context);
                }
            }
            finally
            {
                await next(context);
            }
        }

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
                        Logger.Log($"{userId} disconnected");
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
