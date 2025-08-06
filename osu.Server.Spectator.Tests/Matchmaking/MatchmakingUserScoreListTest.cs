// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer.MatchTypes.Matchmaking;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingUserScoreListTest
    {
        [Fact]
        public void AddPoints()
        {
            MatchmakingUserScoreList list = new MatchmakingUserScoreList();
            list.AddPoints(1, 2, 3);

            Assert.Equal(1, list.Scores.Count);

            Assert.Equal(1, list.Scores[0].UserId);
            Assert.Equal(3, list.Scores[0].Points);
            Assert.Equal(1, list.Scores[0].RoundPlacements.Count);
            Assert.Equal(2, list.Scores[0].RoundPlacements[0]);

            list.AddPoints(1, 5, 6);
            list.AddPoints(2, 8, 9);

            Assert.Equal(2, list.Scores.Count);

            Assert.Equal(1, list.Scores[0].UserId);
            Assert.Equal(9, list.Scores[0].Points);
            Assert.Equal(2, list.Scores[0].RoundPlacements.Count);
            Assert.Equal(2, list.Scores[0].RoundPlacements[0]);
            Assert.Equal(5, list.Scores[0].RoundPlacements[1]);

            Assert.Equal(2, list.Scores[1].UserId);
            Assert.Equal(9, list.Scores[1].Points);
            Assert.Equal(1, list.Scores[1].RoundPlacements.Count);
            Assert.Equal(8, list.Scores[1].RoundPlacements[0]);
        }

        [Fact]
        public void AdjustPlacementsDistinctPoints()
        {
            MatchmakingUserScoreList list = new MatchmakingUserScoreList();

            list.AddPoints(1, 1, 1);
            list.AddPoints(2, 1, 2);
            list.AdjustPlacements();

            Assert.Equal(2, list.Scores[0].Placement);
            Assert.Equal(1, list.Scores[1].Placement);
        }

        [Fact]
        public void AdjustPlacementsRoundTiebreaker()
        {
            MatchmakingUserScoreList list = new MatchmakingUserScoreList();

            list.AddPoints(1, 2, 1);
            list.AddPoints(1, 1, 1);
            list.AddPoints(2, 1, 1);
            list.AddPoints(2, 2, 1);
            list.AdjustPlacements();

            Assert.Equal(2, list.Scores[0].Placement);
            Assert.Equal(1, list.Scores[1].Placement);
        }

        [Fact]
        public void AdjustPlacementsUserIdTiebreaker()
        {
            MatchmakingUserScoreList list = new MatchmakingUserScoreList();

            list.AddPoints(2, 2, 1);
            list.AddPoints(1, 2, 1);
            list.AdjustPlacements();

            Assert.Equal(2, list.Scores[0].Placement);
            Assert.Equal(1, list.Scores[1].Placement);
        }
    }
}
