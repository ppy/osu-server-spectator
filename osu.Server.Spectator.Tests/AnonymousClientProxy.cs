// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace osu.Server.Spectator.Tests
{
    /// <summary>
    /// Proxies objects of type <typeparamref name="T"/> as an anonymous <see cref="IClientProxy"/> object.
    /// Useful in testing where <see cref="IHubContext{THub}"/> is used.
    /// </summary>
    /// <param name="clients">The typed clients object.</param>
    /// <typeparam name="T">The type of clients being proxied.</typeparam>
    public class AnonymousClientProxy<T>(IHubClients<T> clients) : IHubClients
    {
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new ClientProxy(clients.AllExcept(excludedConnectionIds));
        public IClientProxy Client(string connectionId) => new ClientProxy(clients.Client(connectionId));
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new ClientProxy(clients.Clients(connectionIds));
        public IClientProxy Group(string groupName) => new ClientProxy(clients.Group(groupName));
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new ClientProxy(clients.Groups(groupNames));
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new ClientProxy(clients.GroupExcept(groupName, excludedConnectionIds));
        public IClientProxy User(string userId) => new ClientProxy(clients.User(userId));
        public IClientProxy Users(IReadOnlyList<string> userIds) => new ClientProxy(clients.Users(userIds));
        public IClientProxy All => new ClientProxy(clients.All);

        private class ClientProxy(T? target) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = new CancellationToken())
            {
                return target == null
                    ? Task.CompletedTask
                    : (Task)typeof(T).GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!.Invoke(target, args)!;
            }
        }
    }
}
