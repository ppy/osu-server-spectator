// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public partial class MultiplayerHub : IRankedPlayServer
    {
        public async Task DiscardCards(RankedPlayCardItem[] cards)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item!))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                await ((RankedPlayMatchController)room.Controller).DiscardCards(user, cards);
            }
        }

        public async Task PlayCard(RankedPlayCardItem card)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            using (var roomUsage = await getLocalUserRoom(userUsage.Item!))
            {
                var room = roomUsage.Item;
                if (room == null)
                    throw new InvalidOperationException("Attempted to operate on a null room");

                var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());
                if (user == null)
                    throw new InvalidOperationException("Local user was not found in the expected room");

                await ((RankedPlayMatchController)room.Controller).PlayCard(user, card);
            }
        }
    }
}
