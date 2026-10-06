// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay;
using osu.Server.Spectator.Tests.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay
{
    public class RankedPlayMatchControllerTests : MultiplayerTest
    {
        protected ServerMultiplayerRoom Room { get; private set; } = null!;
        protected RankedPlayMatchController MatchController => (RankedPlayMatchController)Room.MatchController;

        protected RankedPlayRoomState RoomState => (RankedPlayRoomState)Room.MatchState!;
        protected RankedPlayUserInfo UserState => RoomState.Users[USER_ID];
        protected RankedPlayUserInfo User2State => RoomState.Users[USER_ID_2];

        public RankedPlayMatchControllerTests()
        {
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

            Database.Setup(db => db.GetAllScoresForPlaylistItem(It.IsAny<long>()))
                    .Returns<long>(_ => Task.FromResult<IEnumerable<SoloScore>>(
                    [
                        new SoloScore { user_id = USER_ID, total_score = 1_000_000 },
                        new SoloScore { user_id = USER_ID_2, total_score = 1_000_000 },
                    ]));
        }

        [Fact]
        public async Task UserModsAppliedOnEnter()
        {
            using (var room = await Rooms.GetForUse(ROOM_ID, true))
            {
                room.Item = await ServerMultiplayerRoom.InitialiseMatchmakingRoomAsync
                (
                    ROOM_ID,
                    RoomController,
                    DatabaseFactory.Object,
                    EventDispatcher,
                    LoggerFactory.Object,
                    new matchmaking_pool(),
                    new[]
                    {
                        new MatchmakingQueueUser(USER_ID.ToString(), new[] { new APIMod { Acronym = "HD" } }) { UserId = USER_ID },
                        new MatchmakingQueueUser(USER_ID_2.ToString()) { UserId = USER_ID_2 }
                    },
                    new MatchmakingBeatmapSelector(new matchmaking_pool(), Enumerable.Range(1, 50).Select(i => new matchmaking_pool_beatmap
                    {
                        id = (uint)i,
                        beatmap_id = i
                    }).ToArray(), new Mock<IDatabaseFactory>().Object),
                    new Mock<IMatchmakingQueueBackgroundService>().Object
                );

                Room = room.Item;
            }

            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser);

            Assert.Equal("HD", Room.Users.Single(u => u.UserID == USER_ID).Mods.Single().Acronym);
            Assert.Empty(Room.Users.Single(u => u.UserID == USER_ID_2).Mods);
        }

        [Fact]
        public async Task UsersCanNotChangeOwnMods()
        {
            using (var room = await Rooms.GetForUse(ROOM_ID, true))
            {
                room.Item = await ServerMultiplayerRoom.InitialiseMatchmakingRoomAsync
                (
                    ROOM_ID,
                    RoomController,
                    DatabaseFactory.Object,
                    EventDispatcher,
                    LoggerFactory.Object,
                    new matchmaking_pool(),
                    new[]
                    {
                        new MatchmakingQueueUser(USER_ID.ToString(), new[] { new APIMod { Acronym = "HD" } }) { UserId = USER_ID },
                        new MatchmakingQueueUser(USER_ID_2.ToString()) { UserId = USER_ID_2 }
                    },
                    new MatchmakingBeatmapSelector(new matchmaking_pool(), Enumerable.Range(1, 50).Select(i => new matchmaking_pool_beatmap
                    {
                        id = (uint)i,
                        beatmap_id = i
                    }).ToArray(), new Mock<IDatabaseFactory>().Object),
                    new Mock<IMatchmakingQueueBackgroundService>().Object
                );

                Room = room.Item;
            }

            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            SetUserContext(ContextUser);

            Assert.Equal("HD", Room.Users.Single(u => u.UserID == USER_ID).Mods.Single().Acronym);
            Assert.Empty(Room.Users.Single(u => u.UserID == USER_ID_2).Mods);

            await Assert.ThrowsAsync<InvalidOperationException>(() => Hub.ChangeUserMods([]));
        }
    }
}
