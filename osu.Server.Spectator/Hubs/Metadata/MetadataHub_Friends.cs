// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public partial class MetadataHub
    {
        private async Task registerFriends(MetadataClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriends(state.UserId))
                    await Groups.AddToGroupAsync(Context.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).FriendConnected(state.UserId);
        }

        private async Task unregisterFriends(MetadataClientState state)
        {
            using (var db = databaseFactory.GetInstance())
            {
                foreach (var friend in await db.GetUserFriends(state.UserId))
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, friend_presence_watchers(friend.zebra_id));
            }

            await Clients.Group(friend_presence_watchers(state.UserId)).FriendDisconnected(state.UserId);
        }

        private static string friend_presence_watchers(int userId) => $"metadata:online-presence-watchers:{userId}";
    }
}
