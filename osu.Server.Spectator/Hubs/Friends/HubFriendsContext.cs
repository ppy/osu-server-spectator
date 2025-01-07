// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Friends;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Friends
{
    public class HubFriendsContext<T>
        where T : class, IFriendsClient
    {
        private readonly IDatabaseFactory databaseFactory;

        public HubFriendsContext(Hub<T> hub, IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;

            Clients = hub.Clients;
            Groups = hub.Groups;
            Context = hub.Context;
        }

        public async Task OnConnectedAsync(ClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriendsAsync(state.UserId))
                    await Groups.AddToGroupAsync(Context.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).FriendConnected(state.UserId);
        }

        public async Task OnDisconnectedAsync(ClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriendsAsync(state.UserId))
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).FriendDisconnected(state.UserId);
        }

        private static string friend_presence_watchers(int userId) => $"friends:online-presence-watchers:{userId}";

        public IHubCallerClients<T> Clients { get; }
        public IGroupManager Groups { get; }
        public HubCallerContext Context { get; }
    }
}
