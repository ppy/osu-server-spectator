// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using osu.Framework.Logging;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Entities;
using StatsdClient;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public abstract class StatefulUserHub<TClient, TUserState> : Hub<TClient>, ILogTarget
        where TUserState : ClientState
        where TClient : class
    {
        protected readonly EntityStore<TUserState> UserStates;

        private readonly Logger logger;

        // ReSharper disable once StaticMemberInGenericType
        private static int totalConnected;

        protected StatefulUserHub(IDistributedCache cache, EntityStore<TUserState> userStates)
        {
            logger = Logger.GetLogger(GetType().Name.Replace("Hub", string.Empty));
            UserStates = userStates;
        }

        protected KeyValuePair<long, TUserState>[] GetAllStates() => UserStates.GetAllEntities();

        /// <summary>
        /// The osu! user id for the currently processing context.
        /// </summary>
        protected int CurrentContextUserId
        {
            get
            {
                if (Context.UserIdentifier == null)
                    throw new InvalidOperationException($"Attempted to get user id with null {nameof(Context.UserIdentifier)}");

                return int.Parse(Context.UserIdentifier);
            }
        }

        public override async Task OnConnectedAsync()
        {
            Log("Connected");
            DogStatsd.Gauge($"{logger.Name}.connected", Interlocked.Increment(ref totalConnected));

            try
            {
                // if a previous connection is still present for the current user, we need to clean it up.
                await cleanUpState(false);
            }
            catch
            {
                Log("State cleanup failed");

                // if any exception happened during clean-up, don't allow the user to reconnect.
                // this limits damage to the user in a bad state if their clean-up cannot occur (they will not be able to reconnect until the issue is resolved).
                Context.Abort();
                throw;
            }

            await base.OnConnectedAsync();
        }

        public sealed override async Task OnDisconnectedAsync(Exception? exception)
        {
            Log("User disconnected");
            DogStatsd.Gauge($"{logger.Name}.connected", Interlocked.Decrement(ref totalConnected));

            await cleanUpState(true);
        }

        private async Task cleanUpState(bool isDisconnect)
        {
            ItemUsage<TUserState>? usage;

            try
            {
                usage = await UserStates.GetForUse(CurrentContextUserId);
            }
            catch (KeyNotFoundException)
            {
                // no state to clean up.
                return;
            }

            Log($"Cleaning up state on {(isDisconnect ? "disconnect" : "connect")}");

            try
            {
                if (usage.Item != null)
                {
                    bool isOurState = usage.Item.ConnectionId == Context.ConnectionId;

                    if (isDisconnect && !isOurState)
                    {
                        // not our state, owned by a different connection.
                        Log("Disconnect state cleanup aborted due to newer connection owning state");
                        return;
                    }

                    try
                    {
                        await CleanUpState(usage.Item);
                    }
                    finally
                    {
                        usage.Destroy();
                        Log("State cleanup completed");
                    }
                }
            }
            finally
            {
                usage.Dispose();
            }
        }

        /// <summary>
        /// Perform any cleanup required on the provided state.
        /// </summary>
        protected virtual Task CleanUpState(TUserState state) => Task.CompletedTask;

        protected async Task<ItemUsage<TUserState>> GetOrCreateLocalUserState()
        {
            var usage = await UserStates.GetForUse(CurrentContextUserId, true);

            if (usage.Item != null && usage.Item.ConnectionId != Context.ConnectionId)
            {
                usage.Dispose();
                throw new InvalidStateException("State is not valid for this connection");
            }

            return usage;
        }

        protected Task<ItemUsage<TUserState>> GetStateFromUser(int userId) => UserStates.GetForUse(userId);

        protected override void Dispose(bool disposing)
        {
            // There is a case where we are logging events post context disposal.
            // This can happen in asynchronous flows where the disposal completes before the async work we are running attempts to log.
            // There's no easy way to check whether the context is in a disposed state, so this is a best effort solution.
            postDisposalLoggableIdentifier = Context.UserIdentifier ?? "???";
            base.Dispose(disposing);
        }

        protected void Log(string message, LogLevel logLevel = LogLevel.Verbose) => logger.Add($"[user:{getLoggableUserIdentifier()}] {message.Trim()}", logLevel);

        protected void Error(string message, Exception exception) => logger.Add($"[user:{getLoggableUserIdentifier()}] {message.Trim()}", LogLevel.Error, exception);

        private string getLoggableUserIdentifier() => postDisposalLoggableIdentifier ?? Context.UserIdentifier ?? "???";

        private string? postDisposalLoggableIdentifier;

        #region Implementation of ILogTarget

        void ILogTarget.Error(string message, Exception exception) => Error(message, exception);
        void ILogTarget.Log(string message, LogLevel logLevel) => Log(message, logLevel);

        #endregion
    }
}
