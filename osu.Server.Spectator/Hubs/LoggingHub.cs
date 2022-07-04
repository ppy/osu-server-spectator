// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Logging;
using Sentry;
using StatsdClient;

namespace osu.Server.Spectator.Hubs;

public class LoggingHub<TClient> : Hub<TClient>, ILogTarget
    where TClient : class
{
    protected string Name;

    private readonly Logger logger;

    // ReSharper disable once StaticMemberInGenericType
    private static int totalConnected;

    public LoggingHub()
    {
        Name = GetType().Name.Replace("Hub", string.Empty);

        logger = Logger.GetLogger(Name);
    }

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
        DogStatsd.Gauge($"{Name}.connected", Interlocked.Increment(ref totalConnected));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Log("User disconnected");
        DogStatsd.Gauge($"{Name}.connected", Interlocked.Decrement(ref totalConnected));

        await base.OnDisconnectedAsync(exception);
    }

    protected void Log(string message, LogLevel logLevel = LogLevel.Verbose) => logger.Add($"[user:{getLoggableUserIdentifier()}] {message.Trim()}", logLevel);

    protected void Error(string message, Exception exception) => logger.Add($"[user:{getLoggableUserIdentifier()}] {message.Trim()}", LogLevel.Error, exception);

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
