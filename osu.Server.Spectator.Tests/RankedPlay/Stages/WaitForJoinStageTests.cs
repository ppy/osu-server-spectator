// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class WaitForJoinStageTests : RankedPlayStageImplementationTest
    {
        public WaitForJoinStageTests()
            : base(RankedPlayStage.WaitForJoin)
        {
        }

        protected override Task JoinUsers()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task HandPopulated()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);

            Assert.True(UserState.Hand.Count > 0);
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(5));
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(5));
            UserReceiver.Invocations.Clear();

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            Assert.True(User2State.Hand.Count > 0);
            UserReceiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Never);
            User2Receiver.Verify(u => u.RankedPlayCardRevealed(It.IsAny<RankedPlayCardItem>(), It.IsAny<MultiplayerPlaylistItem>()), Times.Exactly(5));
        }

        [Fact]
        public async Task ContinuesToRoundWarmupWhenAllPlayersJoined()
        {
            SetUserContext(ContextUser);
            await Hub.JoinRoom(ROOM_ID);
            Assert.Equal(RankedPlayStage.WaitForJoin, RoomState.Stage);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            Assert.Equal(RankedPlayStage.RoundWarmup, RoomState.Stage);
        }
    }
}
