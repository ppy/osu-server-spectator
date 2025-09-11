// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class EloSystemTest
    {
        [Fact]
        public void Basic()
        {
            EloSystem system = new EloSystem { MaxHistory = 10 };

            EloPlayer player1 = new EloPlayer();
            EloPlayer player2 = new EloPlayer();

            system.RecordContest(new EloContest(DateTimeOffset.Now, [
                player1,
                player2
            ]));

            Assert.True(player1.ApproximatePosterior.Mu > 1500);
            Assert.True(player1.ApproximatePosterior.Sig < 350);
            Assert.True(player2.ApproximatePosterior.Mu < 1500);
            Assert.True(player2.ApproximatePosterior.Sig < 350);

            double eloPlayer1 = player1.ApproximatePosterior.Mu;
            double eloPlayer2 = player2.ApproximatePosterior.Mu;

            system.RecordContest(new EloContest(DateTimeOffset.Now, [
                player2,
                player1,
            ]));

            Assert.True(player1.ApproximatePosterior.Mu < eloPlayer1);
            Assert.True(player2.ApproximatePosterior.Mu > eloPlayer2);
        }
    }
}
