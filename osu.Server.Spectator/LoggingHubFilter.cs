// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator
{
    /// <summary>
    /// An <see cref="IHubFilter"/> logging method invoke and error to the <see cref="ILoggingHub"/>.
    /// </summary>
    public class LoggingHubFilter : IHubFilter
    {
        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            var loggingHub = invocationContext.Hub as ILoggingHub;
            var methodCall = $"{invocationContext.HubMethodName}({string.Join(", ", invocationContext.HubMethodArguments.Select(getReadableString))})";

            loggingHub?.Log($"Invoking hub method: {methodCall}");

            try
            {
                return await next(invocationContext);
            }
            catch (Exception e)
            {
                loggingHub?.Error($"Failed to invoke hub method: {methodCall}", e);
                throw;
            }
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
