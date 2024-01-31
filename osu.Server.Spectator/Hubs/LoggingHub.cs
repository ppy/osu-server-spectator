// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sentry;
using StatsdClient;

namespace osu.Server.Spectator.Hubs
{
    public class LoggingHub<TClient> : Hub<TClient>, ILogTarget
        where TClient : class
    {
        protected string Name;

        private readonly ILogger logger;

        // ReSharper disable once StaticMemberInGenericType
        private static int totalConnected;

        public LoggingHub(ILoggerFactory loggerFactory)
        {
            Name = GetType().Name.Replace("Hub", string.Empty);

            logger = loggerFactory.CreateLogger(Name);
        }

        public override async Task OnConnectedAsync()
        {
            Log("Connected");
            DogStatsd.Gauge($"{Name}.connected", Interlocked.Increment(ref totalConnected));
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Log("User disconnected");
            DogStatsd.Gauge($"{Name}.connected", Interlocked.Decrement(ref totalConnected));

            await base.OnDisconnectedAsync(exception);
        }

        protected void Log(string message, LogLevel logLevel = LogLevel.Information) => logger.Log(logLevel, "[user:{userId}] {message}",
            getLoggableUserIdentifier(),
            message.Trim());

        protected void Error(string message, Exception exception) => logger.LogError(exception, "[user:{userId}] {message)}",
            getLoggableUserIdentifier(),
            message.Trim());

        private string getLoggableUserIdentifier() => Context.UserIdentifier ?? "???";

        #region Implementation of ILogTarget

        void ILogTarget.Error(string message, Exception exception)
        {
            Error(message, exception);

            SentrySdk.CaptureException(exception, scope =>
            {
                scope.User = new User
                {
                    Id = Context.UserIdentifier
                };
            });
        }

        void ILogTarget.Log(string message, LogLevel logLevel) => Log(message, logLevel);

        #endregion
    }
}
