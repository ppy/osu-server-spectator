// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerEventLogger
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger<MultiplayerEventLogger> logger;

        public MultiplayerEventLogger(
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory)
        {
            logger = loggerFactory.CreateLogger<MultiplayerEventLogger>();
            this.databaseFactory = databaseFactory;
        }

        public Task LogRoomCreatedAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "room_created",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogRoomDisbandedAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "room_disbanded",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogPlayerJoinedAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "player_joined",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogPlayerLeftAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "player_left",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogPlayerKickedAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "player_kicked",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogHostChangedAsync(long roomId, int userId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "host_changed",
            room_id = roomId,
            user_id = userId,
        });

        public Task LogGameStartedAsync(long roomId, long playlistItemId, MatchStartedEventDetail details) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "game_started",
            room_id = roomId,
            playlist_item_id = playlistItemId,
            event_detail = JsonConvert.SerializeObject(details)
        });

        public Task LogGameAbortedAsync(long roomId, long playlistItemId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "game_aborted",
            room_id = roomId,
            playlist_item_id = playlistItemId,
        });

        public Task LogGameCompletedAsync(long roomId, long playlistItemId) => logEvent(new multiplayer_realtime_room_event
        {
            event_type = "game_completed",
            room_id = roomId,
            playlist_item_id = playlistItemId,
        });

        /// <summary>
        /// Records a user joining a matchmaking room.
        /// </summary>
        public Task LogMatchmakingRoomCreatedAsync(long roomId, MatchmakingRoomCreatedEventDetail details) => logEvent(new matchmaking_room_event
        {
            event_type = "room_created",
            room_id = roomId,
            event_detail = JsonConvert.SerializeObject(details)
        });

        /// <summary>
        /// Records a user joining a matchmaking room.
        /// </summary>
        public Task LogMatchmakingUserJoinAsync(long roomId, int userId) => logEvent(new matchmaking_room_event
        {
            event_type = "user_join",
            room_id = roomId,
            user_id = userId
        });

        /// <summary>
        /// Records a user's individual beatmap selection.
        /// </summary>
        public Task LogMatchmakingUserPickAsync(long roomId, int userId, long playlistItemId) => logEvent(new matchmaking_room_event
        {
            event_type = "user_pick",
            room_id = roomId,
            user_id = userId,
            playlist_item_id = playlistItemId
        });

        /// <summary>
        /// Records the final gameplay beatmap as selected by the server.
        /// </summary>
        public Task LogMatchmakingGameplayBeatmapAsync(long roomId, long playlistItemId) => logEvent(new matchmaking_room_event
        {
            event_type = "gameplay_beatmap",
            room_id = roomId,
            playlist_item_id = playlistItemId
        });

        private async Task logEvent(multiplayer_realtime_room_event ev)
        {
            try
            {
                using var db = databaseFactory.GetInstance();
                await db.LogRoomEventAsync(ev);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to log multiplayer room event to database");
            }
        }

        private async Task logEvent(matchmaking_room_event ev)
        {
            try
            {
                using var db = databaseFactory.GetInstance();
                await db.LogRoomEventAsync(ev);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to log multiplayer room event to database");
            }
        }
    }
}
