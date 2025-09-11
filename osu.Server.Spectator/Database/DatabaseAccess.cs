// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;
using osu.Game.Online.Multiplayer;
using osu.Game.Scoring;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Database
{
    public class DatabaseAccess : IDatabaseAccess
    {
        private MySqlConnection? openConnection;
        private readonly ILogger<DatabaseAccess> logger;
        private readonly ISharedInterop sharedInterop;

        public DatabaseAccess(ILoggerFactory loggerFactory, ISharedInterop sharedInterop)
        {
            logger = loggerFactory.CreateLogger<DatabaseAccess>();
            this.sharedInterop = sharedInterop;
        }

        public async Task<int?> GetUserIdFromTokenAsync(JsonWebToken jwtToken)
        {
            // 直接从 sub claim 获取用户ID
            var userIdClaim = jwtToken.GetClaim("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                //Console.WriteLine("Invalid or missing sub claim in token");
                return null;
            }

            //Console.WriteLine("User ID from token: {0}", userId);

            // 可选：验证用户是否存在和token是否有效
            var connection = await getConnectionAsync();
            var result = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT user_id FROM oauth_tokens WHERE user_id = @userId AND expires_at > UTC_TIMESTAMP()",
                new { userId = userId });

            return result;
        }

        public async Task<string?> GetUsernameAsync(int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<string?>("SELECT username FROM lazer_users WHERE id = @UserID", new { UserID = userId });
        }

        public async Task<bool> IsUserRestrictedAsync(int userId)
        {
            var connection = await getConnectionAsync();

            var priv = await connection.QueryFirstOrDefaultAsync<int>("SELECT priv FROM lazer_users WHERE id = @UserID", new { UserID = userId });

            // priv 值为 1 表示正常用户，其他值可能表示受限用户
            return priv != 1;
        }

        public async Task<multiplayer_room?> GetRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM rooms WHERE id = @RoomID", new { RoomID = roomId });
        }

        public async Task<multiplayer_room?> GetRealtimeRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM rooms WHERE type != 'multiplayer_playlist_items' AND id = @RoomID", new { RoomID = roomId });
        }

        public async Task<database_beatmap?> GetBeatmapAsync(int beatmapId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<database_beatmap>(
                @"SELECT
            id as beatmap_id,
            beatmapset_id,
            checksum,
            beatmap_status as approved,
            difficulty_rating as difficultyrating,
            total_length,
            CASE
                WHEN mode = 'osu' THEN 0
                WHEN mode = 'taiko' THEN 1
                WHEN mode = 'fruits' THEN 2
                WHEN mode = 'mania' THEN 3
                WHEN mode = 'osurx' THEN 4
                WHEN mode = 'osuap' THEN 5
                WHEN mode = 'taikorx' THEN 6
                WHEN mode = 'fruitsrx' THEN 7
                ELSE 0
            END as playmode,
            14 as osu_file_version
        FROM beatmaps
        WHERE id = @BeatmapId AND deleted_at IS NULL",
                new { BeatmapId = beatmapId });
        }

        /// <summary>
        /// 获取谱面，如果不存在则通知 LIO 拉取。
        /// </summary>
        /// <param name="beatmapId">谱面 ID</param>
        /// <returns>谱面信息，如果不存在则返回 null</returns>
        public async Task<database_beatmap?> GetBeatmapOrFetchAsync(int beatmapId)
        {
            var beatmap = await GetBeatmapAsync(beatmapId);
            if (beatmap != null) return beatmap;

            logger.LogDebug("Beatmap {BeatmapId} not found in database, requesting LIO to fetch it", beatmapId);

            try
            {
                await sharedInterop.EnsureBeatmapPresentAsync(beatmapId);
                logger.LogDebug("LIO returned success for beatmap {BeatmapId}, checking database again", beatmapId);
                return await GetBeatmapAsync(beatmapId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LIO request failed for beatmap {BeatmapId}: {ErrorMessage}", beatmapId, ex.Message);
                return null;
            }
        }

        public async Task<fail_time?> GetBeatmapFailTimeAsync(int beatmapId)
        {
            var connection = await getConnectionAsync();
            return (await connection.QuerySingleOrDefaultAsync<fail_time>(
                "SELECT * FROM failtime WHERE beatmap_id = @BeatmapId",
                new { BeatmapId = beatmapId }));
        }

        public async Task UpdateFailTimeAsync(fail_time failTime)
        {
            var connection = await getConnectionAsync();
            await connection.ExecuteAsync(
                @"INSERT INTO failtime (beatmap_id, fail, `exit`)
                VALUES (@BeatmapId, @Fail, @Exit)
                ON DUPLICATE KEY UPDATE fail = @Fail, `exit` = @Exit",
                new { BeatmapId = failTime.beatmap_id, Fail = failTime.fail, Exit = failTime.exit });
        }

        public async Task<int?> GetUserPlaytimeAsync(string gamemode, int userId)
        {
            var connection = await getConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT play_time FROM lazer_user_statistics WHERE user_id = @UserId AND mode = @GameMode", new { UserId = userId, GameMode = gamemode });
        }

        public async Task UpdateUserPlaytimeAsync(string gamemode, int userId, int playTime)
        {
            var connection = await getConnectionAsync();
            await connection.ExecuteAsync("UPDATE lazer_user_statistics SET play_time = @PlayTime WHERE user_id = @UserId AND mode = @GameMode",
                new { UserId = userId, GameMode = gamemode, PlayTime = playTime });
        }

        public async Task<database_beatmap[]> GetBeatmapsAsync(int[] beatmapIds)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<database_beatmap>(
                "SELECT beatmap_id, beatmapset_id, checksum, approved, difficultyrating, playmode, osu_file_version FROM osu_beatmaps WHERE beatmap_id IN @BeatmapIds AND deleted_at IS NULL", new
                {
                    BeatmapIds = beatmapIds
                })).ToArray();
        }

        public async Task<database_beatmap[]> GetBeatmapsAsync(int beatmapSetId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<database_beatmap>(
                "SELECT id as beatmap_id, beatmapset_id, checksum, beatmap_status as approved, difficulty_rating as difficultyrating, mode as playmode, 0 as osu_file_version FROM beatmaps WHERE beatmapset_id = @BeatmapSetId AND deleted_at IS NULL",
                new { BeatmapSetId = beatmapSetId })).ToArray();
        }

        public async Task MarkRoomActiveAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE rooms SET ends_at = null WHERE id = @RoomID", new { RoomID = room.RoomID });
        }

        public async Task UpdateRoomSettingsAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE rooms SET name = @Name, password = @Password, type = @MatchType, queue_mode = @QueueMode WHERE id = @RoomID", new
            {
                RoomID = room.RoomID,
                Name = room.Settings.Name,
                Password = room.Settings.Password,
                // needs ToString() to store as enums correctly, see https://github.com/DapperLib/Dapper/issues/813.
                MatchType = room.Settings.MatchType.ToDatabaseMatchType().ToString(),
                QueueMode = room.Settings.QueueMode.ToDatabaseQueueMode().ToString()
            });
        }

        public async Task UpdateRoomStatusAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE rooms SET status = @Status WHERE id = @RoomID", new
            {
                RoomID = room.RoomID,
                // needs ToString() to store as enums correctly, see https://github.com/DapperLib/Dapper/issues/813.
                Status = room.State.ToDatabaseRoomStatus().ToString(),
            });
        }

        public async Task UpdateRoomHostAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            Debug.Assert(room.Host != null);

            try
            {
                await connection.ExecuteAsync("UPDATE rooms SET host_id = @HostUserID WHERE id = @RoomID", new { HostUserID = room.Host.UserID, RoomID = room.RoomID });
            }
            catch (MySqlException)
            {
                // for now we really don't care about failures in this. it's updating display information each time a user joins/quits and doesn't need to be perfect.
            }
        }

        public async Task AddRoomParticipantAsync(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            var connection = await getConnectionAsync();

            try
            {
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    // the user may have previously been in the room and set some scores, so need to update their presence if existing.
                    await connection.ExecuteAsync(
                        "INSERT INTO room_participated_users (room_id, user_id, joined_at, left_at) VALUES (@RoomID, @UserID, NOW(), NULL) ON DUPLICATE KEY UPDATE left_at = NULL",
                        new { RoomID = room.RoomID, UserID = user.UserID }, transaction);

                    await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count WHERE id = @RoomID", new { RoomID = room.RoomID, Count = room.Users.Count }, transaction);

                    await transaction.CommitAsync();
                }
            }
            catch (MySqlException)
            {
                // for now we really don't care about failures in this. it's updating display information each time a user joins/quits and doesn't need to be perfect.
            }
        }

        public Task AddLoginForUserAsync(int userId, string? userIp)
        {
            return Task.CompletedTask;
            // if (string.IsNullOrEmpty(userIp))
            //     return;
            //
            // var connection = await getConnectionAsync();
            //
            // try
            // {
            //     await connection.ExecuteAsync("INSERT INTO user_login_log (user_id, ip_address, login_method, login_time) VALUES (@UserID, @IP, 'spectator', UTC_TIMESTAMP())",
            //         new { UserID = userId, IP = userIp });
            // }
            // catch (MySqlException ex)
            // {
            //     logger.LogWarning(ex, "Could not log login for user {UserId}", userId);
            // }
        }

        public async Task OfflineUser(int userId)
        {
            var connection = await getConnectionAsync();
            await connection.ExecuteAsync("UPDATE lazer_users SET last_visit = NOW() WHERE `id` = @userId", new { userId = userId });
        }

        public async Task RemoveRoomParticipantAsync(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            var connection = await getConnectionAsync();

            try
            {
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    await connection.ExecuteAsync("UPDATE room_participated_users SET left_at = NOW() WHERE room_id = @RoomID AND user_id = @UserID AND left_at IS NULL",
                        new { RoomID = room.RoomID, UserID = user.UserID }, transaction);

                    await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count WHERE id = @RoomID", new { RoomID = room.RoomID, Count = room.Users.Count }, transaction);

                    await transaction.CommitAsync();
                }
            }
            catch (MySqlException)
            {
                // for now we really don't care about failures in this. it's updating display information each time a user joins/quits and doesn't need to be perfect.
            }
        }

        public async Task<multiplayer_playlist_item> GetPlaylistItemAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleAsync<multiplayer_playlist_item>(
                "SELECT * FROM room_playlists WHERE id = @Id AND room_id = @RoomId",
                new { Id = playlistItemId, RoomId = roomId });

            // TODO: 后面补充修改
            /*
            return await connection.QuerySingleAsync<multiplayer_playlist_item>(
                "SELECT `i`.*, `b`.`checksum`, `b`.`difficultyrating` " +
                "FROM `multiplayer_playlist_items` `i` " +
                "JOIN `osu_beatmaps` `b` " +
                "ON `b`.`beatmap_id` = `i`.`beatmap_id` " +
                "WHERE `i`.`id` = @Id " +
                "AND `i`.`room_id` = @RoomId", 
                new { Id = playlistItemId, RoomId = roomId });
            */
        }


        public async Task<long> AddPlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            // 计算该房间内的下一个逻辑 id，并在同一条 INSERT 中使用
            // 同时显式写入 expired / played_at，避免 NOT NULL 无默认值的问题
            await connection.ExecuteAsync(@"
        INSERT INTO room_playlists
            (id, owner_id, room_id, beatmap_id, ruleset_id,
             allowed_mods, required_mods, freestyle, playlist_order,
             expired, played_at)
        VALUES
            (
                (SELECT COALESCE(MAX(rp.id), -1) + 1
                 FROM room_playlists rp
                 WHERE rp.room_id = @room_id),
                @owner_id, @room_id, @beatmap_id, @ruleset_id,
                @allowed_mods, @required_mods, @freestyle, @playlist_order,
                @expired, @played_at
            );",
                item);

            // 返回刚插入行的“逻辑 id”（不是自增主键 db_id）
            // 通过 LAST_INSERT_ID() 关联取回那一行的 id
            return await connection.QuerySingleAsync<long>(@"
        SELECT id FROM room_playlists WHERE db_id = LAST_INSERT_ID();");
        }

        public async Task UpdatePlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(@"
        UPDATE room_playlists SET
            beatmap_id     = @beatmap_id,
            ruleset_id     = @ruleset_id,
            required_mods  = @required_mods,
            allowed_mods   = @allowed_mods,
            freestyle      = @freestyle,
            playlist_order = @playlist_order,
            expired        = @expired,
            played_at      = @played_at,
            updated_at     = NOW()
        WHERE id = @id AND room_id = @room_id;", item);
        }

        public async Task RemovePlaylistItemAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "DELETE FROM room_playlists WHERE id = @Id AND room_id = @RoomId",
                new { Id = playlistItemId, RoomId = roomId });
        }

        public async Task MarkPlaylistItemAsPlayedAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(@"
        UPDATE room_playlists
        SET expired = 1, played_at = NOW(), updated_at = NOW()
        WHERE id = @PlaylistItemId AND room_id = @RoomId;",
                new { PlaylistItemId = playlistItemId, RoomId = roomId });
        }

        public async Task EndMatchAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            // Expire all non-expired items from the playlist.
            // We're not removing them because they may be linked to other tables (e.g. `multiplayer_realtime_room_events`, `multiplayer_scores_high`, etc.)
            // TODO: Re-enable this when `osu_api.multiplayer_score_links` exists. + " AND (SELECT COUNT(*) FROM multiplayer_score_links l WHERE l.playlist_item_id = p.id) = 0"
            await connection.ExecuteAsync(
                "UPDATE room_playlists p"
                + " SET p.expired = 1, played_at = NOW(), updated_at = NOW()"
                + " WHERE p.room_id = @RoomID"
                + " AND p.expired = 0",
                new { RoomID = room.RoomID });

            int totalUsers = connection.QuerySingle<int>("SELECT COUNT(*) FROM room_participated_users WHERE room_id = @RoomID", new { RoomID = room.RoomID });

            // Close the room.
            await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count, ends_at = NOW() WHERE id = @RoomID", new { RoomID = room.RoomID, Count = totalUsers, });
        }

        public async Task<multiplayer_playlist_item[]> GetAllPlaylistItemsAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<multiplayer_playlist_item>(
                "SELECT * FROM room_playlists WHERE room_id = @RoomId",
                new { RoomId = roomId }
            )).ToArray();

            // TODO: 后面补充修改
            /*
            return (await connection.QueryAsync<multiplayer_playlist_item>(
                "SELECT `i`.*, `b`.`checksum`, `b`.`difficultyrating` " +
                "FROM `multiplayer_playlist_items` `i` " +
                "JOIN `osu_beatmaps` `b` " +
                "ON `b`.`beatmap_id` = `i`.`beatmap_id` " +
                "WHERE `i`.`room_id` = @RoomId",
                new { RoomId = roomId }
            )).ToArray();
            */
}


        public async Task MarkScoreHasReplay(Score score)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE `scores` SET `has_replay` = 1 WHERE `id` = @scoreId", new { scoreId = score.ScoreInfo.OnlineID, });
        }

        public async Task<SoloScore?> GetScoreFromTokenAsync(long token)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<SoloScore?>(
                "SELECT * FROM `scores` WHERE `id` = (SELECT `score_id` FROM `score_tokens` WHERE `id` = @Id)", new { Id = token });
        }

        public async Task<SoloScore?> GetScoreAsync(long id)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<SoloScore?>("SELECT * FROM `scores` WHERE `id` = @Id", new { Id = id });
        }

        public async Task<bool> IsScoreProcessedAsync(long scoreId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<bool>("SELECT 1 FROM `scores` WHERE `id` = @ScoreId AND `processed` = '1'", new { ScoreId = scoreId });
        }

        public async Task<phpbb_zebra?> GetUserRelation(int userId, int zebraId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server uses relationship table instead of phpbb_zebra
            var relationship = await connection.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM `relationship` WHERE `user_id` = @UserId AND `target_id` = @ZebraId",
                new { UserId = userId, ZebraId = zebraId });

            if (relationship == null)
                return null;

            // Convert relationship to phpbb_zebra format for compatibility
            return new phpbb_zebra { user_id = userId, zebra_id = zebraId, friend = relationship.type == "Friend", foe = relationship.type == "Block" };
        }

        public async Task<IEnumerable<int>> GetUserFriendsAsync(int userId)
        {
            var connection = await getConnectionAsync();

            // Query adapted for g0v0-server schema using relationship table
            return await connection.QueryAsync<int>(
                "SELECT r.target_id FROM relationship r "
                + "JOIN lazer_users u ON r.target_id = u.id "
                + "WHERE r.user_id = @UserId "
                + "AND r.type = 'Friend' "
                + "AND u.priv = 1", new { UserId = userId });
        }

        public async Task<bool> GetUserAllowsPMs(int userId)
        {
            var connection = await getConnectionAsync();

            // 在g0v0-server中，使用pm_friends_only字段（false表示允许所有人发送PM）
            var pmFriendsOnly = await connection.QuerySingleOrDefaultAsync<bool>("SELECT `pm_friends_only` FROM `lazer_users` WHERE `id` = @UserId", new { UserId = userId });

            // 如果pm_friends_only为false，表示允许所有人发送PM
            return !pmFriendsOnly;
        }

        public async Task<osu_build?> GetBuildByIdAsync(int buildId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server doesn't have osu_builds table, return a dummy build
            return new osu_build { build_id = (uint)buildId, version = "unknown", hash = null, users = 0 };
        }

        public Task<IEnumerable<osu_build>> GetAllMainLazerBuildsAsync()
        {
            // g0v0-server doesn't have osu_builds table, return empty list
            return Task.FromResult<IEnumerable<osu_build>>(new List<osu_build>());
        }

        public Task<IEnumerable<osu_build>> GetAllPlatformSpecificLazerBuildsAsync()
        {
            // g0v0-server doesn't have osu_builds table, return empty list
            return Task.FromResult<IEnumerable<osu_build>>(new List<osu_build>());
        }

        public Task UpdateBuildUserCountAsync(osu_build build)
        {
            // g0v0-server doesn't have osu_builds table, do nothing
            return Task.CompletedTask;
        }

        public Task<IEnumerable<chat_filter>> GetAllChatFiltersAsync()
        {
            // g0v0-server doesn't have chat_filters table, return empty list
            return Task.FromResult<IEnumerable<chat_filter>>(new List<chat_filter>());
        }

        public async Task<IEnumerable<multiplayer_room>> GetActiveDailyChallengeRoomsAsync()
        {
            var connection = await getConnectionAsync();

            return await connection.QueryAsync<multiplayer_room>(
                "SELECT * FROM `rooms` "
                + "WHERE `category` = 'daily_challenge' "
                + "AND `type` = 'playlists' "
                + "AND `starts_at` <= NOW() "
                + "AND `ends_at` > NOW()");
        }

        public async Task<(long roomID, long playlistItemID)?> GetMultiplayerRoomIdForScoreAsync(long scoreId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<(long, long)?>(
                "SELECT `room_id`, `playlist_item_id` "
                + "FROM `scores` "
                + "WHERE `id` = @scoreId",
                new { scoreId = scoreId });
        }

        /// <summary>
        /// Retrieves ALL score data for scores on a playlist item.
        /// </summary>
        /// <remarks>
        /// This should be used sparingly as it queries full rows.
        /// </remarks>
        /// <param name="playlistItemId">The playlist item.</param>
        public async Task<IEnumerable<SoloScore>> GetAllScoresForPlaylistItem(long playlistItemId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<SoloScore>(
                "SELECT * FROM `scores` "
                + "JOIN `multiplayer_score_links` ON `multiplayer_score_links`.`score_id` = `scores`.`id` "
                + "WHERE `multiplayer_score_links`.`playlist_item_id` = @playlistItemId", new
                {
                    playlistItemId = playlistItemId
                }));
        }

        /// <summary>
        /// Retrieves the <see cref="SoloScore.id">ID</see> and <see cref="SoloScore.total_score">total score</see> for passing scores on a playlist item.
        /// </summary>
        /// <param name="roomId">The room ID.</param>
        /// <param name="playlistItemId">The playlist item.</param>
        /// <param name="afterScoreId">The score ID after which to retrieve.</param>
        public async Task<IEnumerable<SoloScore>> GetPassingScoresForPlaylistItem(long roomId, long playlistItemId, ulong afterScoreId = 0)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<SoloScore>(
                "SELECT `scores`.`id`, `scores`.`total_score` FROM `scores` "
                + "JOIN `playlist_best_scores` ON `playlist_best_scores`.`score_id` = `scores`.`id` "
                + "WHERE `scores`.`passed` = 1 "
                + "AND `playlist_best_scores`.`playlist_id` = @playlistItemId "
                + "AND `playlist_best_scores`.`room_id` = @roomId "
                + "AND `playlist_best_scores`.`score_id` > @afterScoreId "
                , new { playlistItemId = playlistItemId, afterScoreId = afterScoreId, roomId = roomId }));
        }

        public async Task<playlist_best_score?> GetUserBestScoreAsync(long roomId, long playlistItemId, int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<playlist_best_score>(
                "SELECT * FROM `playlist_best_scores` WHERE `playlist_id` = @playlistItemId AND `user_id` = @userId AND `room_id` = @roomId",
                new { playlistItemId = playlistItemId, userId = userId, roomId = roomId });
        }

        public async Task<int> GetUserRankInRoomAsync(long roomId, long playlistItemId, ulong scoreId)
        {
            var connection = await getConnectionAsync();
            // https://github.com/GooGuTeam/g0v0-server/blob/main/app/database/playlist_best_score.py#L78
            return await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT sub.row_number"
                + "FROM ("
                + "   SELECT "
                + "        score_id,"
                + "        ROW_NUMBER() OVER ("
                + "            PARTITION BY playlist_id, room_id "
                + "            ORDER BY total_score DESC"
                + "        ) AS row_number"
                + "    FROM playlist_best_scores"
                + "    WHERE playlist_id = @playlistItemId AND room_id = @roomId" + ") AS sub"
                + "WHERE sub.score_id = @scoreId;",
                new { playlistItemId = playlistItemId, roomId = roomId, scoreId = scoreId });
        }

        public async Task LogRoomEventAsync(multiplayer_realtime_room_event ev)
        {
            var connection = await getConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO `multiplayer_events` (`room_id`, `event_type`, `playlist_item_id`, `user_id`, `event_detail`, `created_at`, `updated_at`) "
                + "VALUES (@room_id, @event_type, @playlist_item_id, @user_id, @event_detail, NOW(), NOW())",
                ev);
        }

        public async Task ToggleUserPresenceAsync(int userId, bool visible)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "UPDATE `lazer_users` SET `is_active` = @visible WHERE `id` = @userId",
                new
                {
                    visible = visible,
                    userId = userId
                });
        }

        public async Task<float> GetUserPPAsync(int userId, int rulesetId)
        {
            var connection = await getConnectionAsync();
            
            // 使用 lazer_user_statistics 表存储按模式分类的用户统计数据
            string gameMode = rulesetId switch
            {
                0 => "osu",
                1 => "taiko", 
                2 => "fruits",
                3 => "mania",
                _ => throw new ArgumentOutOfRangeException(nameof(rulesetId), rulesetId, null)
            };
            
            // 从 lazer_user_statistics 表获取用户在指定模式下的PP值
            var ppValue = await connection.QuerySingleOrDefaultAsync<float?>(
                "SELECT pp FROM lazer_user_statistics WHERE user_id = @userId AND mode = @gameMode", 
                new { userId = userId, gameMode = gameMode });
            
            // 如果该模式下没有PP数据，返回0（表示用户在该模式下没有成绩）
            return ppValue ?? 0;
        }

        public async Task<matchmaking_pool[]> GetActiveMatchmakingPoolsAsync()
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<matchmaking_pool>("SELECT * FROM `matchmaking_pools` WHERE `active` = 1")).ToArray();
        }

        public async Task<matchmaking_pool?> GetMatchmakingPoolAsync(int poolId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<matchmaking_pool>("SELECT * FROM `matchmaking_pools` WHERE `id` = @PoolId", new
            {
                PoolId = poolId
            });
        }

        public async Task<matchmaking_pool_beatmap[]> GetMatchmakingPoolBeatmapsAsync(int poolId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server 使用 beatmaps 表而不是 osu_beatmaps 表
            return (await connection.QueryAsync<matchmaking_pool_beatmap>("SELECT p.*, b.checksum, b.difficulty_rating as difficultyrating FROM `matchmaking_pool_beatmaps` p "
                                                                          + "JOIN `beatmaps` b ON p.beatmap_id = b.id "
                                                                          + "WHERE p.pool_id = @PoolId AND b.deleted_at IS NULL", new
            {
                PoolId = poolId
            })).ToArray();
        }

        public async Task<matchmaking_user_stats?> GetMatchmakingUserStatsAsync(int userId, int rulesetId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<matchmaking_user_stats>("SELECT * FROM `matchmaking_user_stats` WHERE `user_id` = @UserId AND `ruleset_id` = @RulesetId", new
            {
                UserId = userId,
                RulesetId = rulesetId
            });
        }

        public async Task UpdateMatchmakingUserStatsAsync(matchmaking_user_stats stats)
        {
            var connection = await getConnectionAsync();

            // 现在 g0v0-server 的 matchmaking_user_stats 表已经有 created_at 和 updated_at 字段
            await connection.ExecuteAsync("INSERT INTO `matchmaking_user_stats` (`user_id`, `ruleset_id`, `first_placements`, `total_points`, `elo_data`, `created_at`, `updated_at`) "
                                          + "VALUES (@UserId, @RulesetId, @FirstPlacements, @TotalPoints, @EloData, NOW(), NOW()) "
                                          + "ON DUPLICATE KEY UPDATE "
                                          + "`first_placements` = @FirstPlacements, "
                                          + "`total_points` = @TotalPoints, "
                                          + "`elo_data` = @EloData, "
                                          + "`updated_at` = NOW()", new
            {
                UserId = stats.user_id,
                RulesetId = stats.ruleset_id,
                FirstPlacements = stats.first_placements,
                TotalPoints = stats.total_points,
                EloData = stats.elo_data
            });
        }

        public void Dispose()
        {
            openConnection?.Dispose();
        }

        private async Task<MySqlConnection> getConnectionAsync()
        {
            if (openConnection != null)
                return openConnection;

            DapperExtensions.InstallDateTimeOffsetMapper();

            // 构建连接字符串，如果有密码则包含密码
            string connectionString = string.IsNullOrEmpty(AppSettings.DatabasePassword)
                ? $"Server={AppSettings.DatabaseHost};Port={AppSettings.DatabasePort};Database={AppSettings.DatabaseName};User ID={AppSettings.DatabaseUser};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;Pipelining=false"
                : $"Server={AppSettings.DatabaseHost};Port={AppSettings.DatabasePort};Database={AppSettings.DatabaseName};User ID={AppSettings.DatabaseUser};Password={AppSettings.DatabasePassword};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;Pipelining=false";

            //打印连接字符串
            //logger.LogInformation("Connecting to database: {ConnectionString}", connectionString);

            openConnection = new MySqlConnection(connectionString);

            await openConnection.OpenAsync();

            return openConnection;
        }
    }
}