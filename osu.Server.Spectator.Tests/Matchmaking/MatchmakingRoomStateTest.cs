// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingRoomStateTest
    {
        [Fact]
        public void AddPoints()
        {
            MatchmakingRoomState list = new MatchmakingRoomState();
            list.NextRound();
            list.SetScore(1, 2, 3, new SoloScoreInfo());

            Assert.Equal(1, list.Users.Count);

            Assert.Equal(3, list.Users[1].Points);
            Assert.Equal(1, list.Users[1].Rounds.Count);
            Assert.Equal(2, list.Users[1].Rounds[1].Placement);

            list.SetScore(1, 5, 6, new SoloScoreInfo());
            list.SetScore(2, 8, 9, new SoloScoreInfo());

            Assert.Equal(2, list.Users.Count);

            Assert.Equal(9, list.Users[1].Points);
            Assert.Equal(2, list.Users[1].Rounds.Count);
            Assert.Equal(2, list.Users[1].Rounds[0].Placement);
            Assert.Equal(5, list.Users[1].Rounds[1].Placement);

            Assert.Equal(9, list.Users[2].Points);
            Assert.Equal(1, list.Users[2].Rounds.Count);
            Assert.Equal(8, list.Users[2].Rounds[0].Placement);
        }

        [Fact]
        public void AdjustPlacementsDistinctPoints()
        {
            MatchmakingRoomState list = new MatchmakingRoomState();

            list.SetScore(1, 1, 1, new SoloScoreInfo());
            list.SetScore(2, 1, 2, new SoloScoreInfo());
            list.ComputePlacements();

            Assert.Equal(1, list.Users[2].Placement);
            Assert.Equal(2, list.Users[1].Placement);
        }

        [Fact]
        public void AdjustPlacementsRoundTiebreaker()
        {
            MatchmakingRoomState list = new MatchmakingRoomState();

            list.SetScore(1, 2, 1, new SoloScoreInfo());
            list.SetScore(2, 1, 1, new SoloScoreInfo());
            list.NextRound();
            list.SetScore(1, 1, 1, new SoloScoreInfo());
            list.SetScore(2, 2, 1, new SoloScoreInfo());
            list.ComputePlacements();

            Assert.Equal(1, list.Users[2].Placement);
            Assert.Equal(2, list.Users[1].Placement);
        }

        [Fact]
        public void AdjustPlacementsUserIdTiebreaker()
        {
            MatchmakingRoomState list = new MatchmakingRoomState();

            list.SetScore(2, 2, 1, new SoloScoreInfo());
            list.SetScore(1, 2, 1, new SoloScoreInfo());
            list.ComputePlacements();

            Assert.Equal(1, list.Users[1].Placement);
            Assert.Equal(2, list.Users[2].Placement);
        }
    }
}
