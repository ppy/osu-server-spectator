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
        public void Basic()
        {
            var state = new MatchmakingRoomState();

            // 1 -> 3 -> 2

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 2, TotalScore = 500 },
                new SoloScoreInfo { UserID = 1, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 3, TotalScore = 750 },
            ]);

            Assert.Equal(8, state.Users[1].Points);
            Assert.Equal(1, state.Users[1].Placement);
            Assert.Equal(1, state.Users[1].Rounds[1].Placement);

            Assert.Equal(6, state.Users[2].Points);
            Assert.Equal(3, state.Users[2].Placement);
            Assert.Equal(3, state.Users[2].Rounds[1].Placement);

            Assert.Equal(7, state.Users[3].Points);
            Assert.Equal(2, state.Users[3].Placement);
            Assert.Equal(2, state.Users[3].Rounds[1].Placement);

            // 2 -> 1 -> 3

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 2, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 1, TotalScore = 750 },
                new SoloScoreInfo { UserID = 3, TotalScore = 500 },
            ]);

            Assert.Equal(15, state.Users[1].Points);
            Assert.Equal(1, state.Users[1].Placement);
            Assert.Equal(2, state.Users[1].Rounds[2].Placement);

            Assert.Equal(14, state.Users[2].Points);
            Assert.Equal(2, state.Users[2].Placement);
            Assert.Equal(1, state.Users[2].Rounds[2].Placement);

            Assert.Equal(13, state.Users[3].Points);
            Assert.Equal(3, state.Users[3].Placement);
            Assert.Equal(3, state.Users[3].Rounds[2].Placement);
        }

        [Fact]
        public void MatchingScores()
        {
            var state = new MatchmakingRoomState();

            // 1 + 2 -> 3 + 4

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 1, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 2, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 3, TotalScore = 500 },
                new SoloScoreInfo { UserID = 4, TotalScore = 500 },
            ]);

            Assert.Equal(7, state.Users[1].Points);
            Assert.Equal(1, state.Users[1].Placement);
            Assert.Equal(2, state.Users[1].Rounds[1].Placement);

            Assert.Equal(7, state.Users[2].Points);
            Assert.Equal(2, state.Users[2].Placement);
            Assert.Equal(2, state.Users[2].Rounds[1].Placement);

            Assert.Equal(5, state.Users[3].Points);
            Assert.Equal(3, state.Users[3].Placement);
            Assert.Equal(4, state.Users[3].Rounds[1].Placement);

            Assert.Equal(5, state.Users[4].Points);
            Assert.Equal(4, state.Users[4].Placement);
            Assert.Equal(4, state.Users[4].Rounds[1].Placement);
        }

        [Fact]
        public void RoundTieBreaker()
        {
            var state = new MatchmakingRoomState();

            // 1 -> 2

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 1, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 2, TotalScore = 500 },
            ]);

            // 2 -> 1

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 1, TotalScore = 500 },
                new SoloScoreInfo { UserID = 2, TotalScore = 1000 },
            ]);

            Assert.Equal(1, state.Users[1].Placement);
            Assert.Equal(2, state.Users[2].Placement);
        }

        [Fact]
        public void UserIdTieBreaker()
        {
            var state = new MatchmakingRoomState();

            // 1 + 2 + 3 + 4 + 5 + 6

            state.NextRound();
            state.SetScores(
            [
                new SoloScoreInfo { UserID = 4, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 6, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 2, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 3, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 1, TotalScore = 1000 },
                new SoloScoreInfo { UserID = 5, TotalScore = 1000 },
            ]);

            Assert.Equal(1, state.Users[1].Placement);
            Assert.Equal(2, state.Users[2].Placement);
            Assert.Equal(3, state.Users[3].Placement);
            Assert.Equal(4, state.Users[4].Placement);
            Assert.Equal(5, state.Users[5].Placement);
            Assert.Equal(6, state.Users[6].Placement);
        }
    }
}
