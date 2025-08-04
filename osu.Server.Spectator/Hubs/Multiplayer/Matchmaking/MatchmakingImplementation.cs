// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    public class MatchmakingImplementation : MatchTypeImplementation
    {
        public const int MATCHMAKING_ROOM_SIZE = 1;

        private readonly MatchmakingRoomState state;

        public MatchmakingImplementation(ServerMultiplayerRoom room, IMultiplayerHubContext hub)
            : base(room, hub)
        {
            room.MatchState = state = new MatchmakingRoomState();
        }

        public override async Task Initialise()
        {
            await base.Initialise();
            await Hub.NotifyMatchRoomStateChanged(Room);
        }

        public override MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.matchmaking
        };
    }
}
