// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Friends;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Friends
{
    public class MetadataHubFriendsContext<THub, T>
        where THub : Hub<T>
        where T : class, IFriendsClient
    {
        private readonly IDatabaseFactory databaseFactory;

        public MetadataHubFriendsContext(IHubContext<THub> context, IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            Clients = context.Clients;
            Groups = context.Groups;
        }

        public async Task OnConnectedAsync(ClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriendsAsync(state.UserId))
                    await Groups.AddToGroupAsync(state.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).SendAsync(nameof(IFriendsClient.FriendConnected), state.UserId);
        }

        public async Task OnDisconnectedAsync(ClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriendsAsync(state.UserId))
                    await Groups.RemoveFromGroupAsync(state.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).SendAsync(nameof(IFriendsClient.FriendDisconnected), state.UserId);
        }

        private static string friend_presence_watchers(int userId) => $"friends:online-presence-watchers:{userId}";

        public IHubClients Clients { get; }
        public IGroupManager Groups { get; }
    }
}
