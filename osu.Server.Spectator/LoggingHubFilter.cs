// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    /// <summary>
    /// An <see cref="IHubFilter"/> logging method invoke and error to the <see cref="ILogTarget"/>.
    /// </summary>
    public class LoggingHubFilter : IHubFilter
    {
        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (!(invocationContext.Hub is ILogTarget logTarget))
                throw new InvalidOperationException($"Hub implementation {invocationContext.Hub.GetType().Name} doesn't implement {nameof(ILogTarget)}.");

            try
            {
                if (DebugUtils.IsDebugBuild)
                    logTarget.Log($"Invoking hub method: {getMethodCallDisplayString(invocationContext)}", LogLevel.Debug);

                return await next(invocationContext);
            }
            catch (Exception e)
            {
                logTarget.Error($"Failed to invoke hub method: {getMethodCallDisplayString(invocationContext)}", e);
                throw;
            }
        }

        private static string getMethodCallDisplayString(HubInvocationContext invocationContext)
        {
            var methodCall = $"{invocationContext.HubMethodName}({string.Join(", ", invocationContext.HubMethodArguments.Select(getReadableString))})";
            return methodCall;
        }

        private static string? getReadableString(object? value)
        {
            switch (value)
            {
                case null:
                    return "null";

                case string str:
                    return $"\"{str}\"";

                case IEnumerable enumerable:
                    return $"{{ {string.Join(", ", enumerable.Cast<object?>().Select(getReadableString))} }}";

                default:
                    return value.ToString();
            }
        }
    }
}
