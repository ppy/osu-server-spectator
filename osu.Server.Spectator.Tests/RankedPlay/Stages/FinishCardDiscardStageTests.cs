// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class FinishCardDiscardStageTests : RankedPlayStageImplementationTest
    {
        public FinishCardDiscardStageTests()
            : base(RankedPlayStage.FinishCardDiscard)
        {
        }

        [Fact]
        public async Task ContinuesToCardPlay()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);
        }
        
    }
}
