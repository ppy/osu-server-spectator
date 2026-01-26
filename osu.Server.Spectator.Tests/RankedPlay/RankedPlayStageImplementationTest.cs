// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay
{
    /// <summary>
    /// Abstract class for testing a <see cref="RankedPlayStageImplementation"/>.
    /// </summary>
    public abstract class RankedPlayStageImplementationTest : MultiplayerTest, IAsyncLifetime
    {
        protected ServerMultiplayerRoom Room { get; private set; } = null!;
        protected RankedPlayMatchController MatchController => (RankedPlayMatchController)Room.MatchController;

        protected RankedPlayRoomState RoomState => (RankedPlayRoomState)Room.MatchState!;
        protected RankedPlayUserInfo UserState => RoomState.Users[USER_ID];
        protected RankedPlayUserInfo User2State => RoomState.Users[USER_ID_2];

        private readonly RankedPlayStage stage;

        protected RankedPlayStageImplementationTest(RankedPlayStage stage)
        {
            this.stage = stage;

            AppSettings.MatchmakingRoomRounds = 2;
            AppSettings.MatchmakingRoomAllowSkip = true;

            Database.Setup(db => db.GetRealtimeRoomAsync(ROOM_ID))
                    .Callback<long>(roomId => InitialiseRoom(roomId, 20))
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.ranked_play,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = int.Parse(Hub.Context.UserIdentifier!),
                    });

            Database.Setup(db => db.GetMatchmakingUserStatsAsync(It.IsAny<int>(), It.IsAny<uint>()))
                    .Returns<int, uint>((userId, poolId) => Task.FromResult<matchmaking_user_stats?>(new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        pool_id = poolId
                    }));
        }

        public async Task InitializeAsync()
        {
            using (var room = await Rooms.GetForUse(ROOM_ID, true))
            {
                room.Item = await ServerMultiplayerRoom.InitialiseMatchmakingRoomAsync(ROOM_ID, RoomController, DatabaseFactory.Object, EventDispatcher, LoggerFactory.Object, 0,
                    [USER_ID, USER_ID_2], new MatchmakingBeatmapSelector(Enumerable.Range(1, 50).Select(i => new matchmaking_pool_beatmap
                    {
                        id = (uint)i,
                        beatmap_id = i
                    }).ToArray()));

                Room = room.Item;
            }

            await JoinUsers();
            await SetupForEnter();

            if (RoomState.Stage != stage)
                await MatchController.GotoStage(stage);
        }

        protected virtual async Task JoinUsers()
        {
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser);
        }

        protected virtual Task SetupForEnter()
        {
            return Task.CompletedTask;
        }

        protected async Task FinishCountdown()
        {
            await Room.SkipToEndOfCountdown(Room.FindCountdownOfType<RankedPlayStageCountdown>());
        }

        [Fact]
        public virtual async Task ContinuesToEndedWhenAnyPlayerLeaves()
        {
            SetUserContext(ContextUser);

            try
            {
                await Hub.JoinRoom(ROOM_ID);
            }
            catch
            {
            }

            await Hub.LeaveRoom();

            switch (stage)
            {
                case RankedPlayStage.WaitForJoin:
                    Assert.Equal(0, RoomState.CurrentRound);
                    Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
                    Assert.Equal(1_000_000, UserState.Life);
                    break;

                case RankedPlayStage.Gameplay:
                case RankedPlayStage.Results:
                    Assert.Equal(stage, RoomState.Stage);
                    Assert.Equal(0, UserState.Life);
                    break;

                default:
                    Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
                    Assert.Equal(0, UserState.Life);
                    break;
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
