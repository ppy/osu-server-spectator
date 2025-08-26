// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Implementation of a head-to-head room.
    /// </summary>
    public class HeadToHead : MultiplayerTypeImplementation
    {
        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerHubContext hub;

        public HeadToHead(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory)
            : base(room, hub, dbFactory)
        {
            this.room = room;
            this.hub = hub;
        }

        public override async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            await base.HandleUserJoined(user);

            if (user.MatchState != null)
            {
                // we don't need a state, but keep things simple by completely nulling the state.
                // this allows the client to see a user state change and handle match type specifics based on that alone.
                user.MatchState = null;
                await hub.NotifyMatchUserStateChanged(room, user);
            }
        }

        public override MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.head_to_head
        };
    }
}
