// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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

namespace osu.Server.Spectator.Database
{
    public class DatabaseAccess : IDatabaseAccess
    {
        private MySqlConnection? openConnection;
        private readonly ILogger<DatabaseAccess> logger;

        public DatabaseAccess(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<DatabaseAccess>();
        }

        public async Task<int?> GetUserIdFromTokenAsync(JsonWebToken jwtToken)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<int?>("SELECT user_id FROM oauth_tokens WHERE expires_at > now() AND access_token = @id",
                new { id = jwtToken.Id });
        }

        public async Task<string?> GetUsernameAsync(int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<string?>("SELECT username FROM lazer_users WHERE id = @UserID", new
            {
                UserID = userId
            });
        }

        public async Task<bool> IsUserRestrictedAsync(int userId)
        {
            var connection = await getConnectionAsync();

            var priv = await connection.QueryFirstOrDefaultAsync<int>("SELECT priv FROM lazer_users WHERE id = @UserID", new
            {
                UserID = userId
            });

            // priv 值为 1 表示正常用户，其他值可能表示受限用户
            return priv != 1;
        }

        public async Task<multiplayer_room?> GetRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM rooms WHERE id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public async Task<multiplayer_room?> GetRealtimeRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM rooms WHERE type != 'playlists' AND id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public async Task<database_beatmap?> GetBeatmapAsync(int beatmapId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<database_beatmap>(
                "SELECT id as beatmap_id, beatmapset_id, checksum, beatmap_status as approved, difficulty_rating as difficultyrating, mode as playmode, 0 as osu_file_version FROM beatmaps WHERE id = @BeatmapId AND deleted_at IS NULL", new
                {
                    BeatmapId = beatmapId
                });
        }

        public async Task<database_beatmap[]> GetBeatmapsAsync(int beatmapSetId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<database_beatmap>(
                "SELECT id as beatmap_id, beatmapset_id, checksum, beatmap_status as approved, difficulty_rating as difficultyrating, mode as playmode, 0 as osu_file_version FROM beatmaps WHERE beatmapset_id = @BeatmapSetId AND deleted_at IS NULL", new
                {
                    BeatmapSetId = beatmapSetId
                })).ToArray();
        }

        public async Task MarkRoomActiveAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE rooms SET ends_at = null WHERE id = @RoomID", new
            {
                RoomID = room.RoomID
            });
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
                await connection.ExecuteAsync("UPDATE rooms SET host_id = @HostUserID WHERE id = @RoomID", new
                {
                    HostUserID = room.Host.UserID,
                    RoomID = room.RoomID
                });
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
                    await connection.ExecuteAsync("INSERT INTO room_participated_users (room_id, user_id, joined_at, left_at) VALUES (@RoomID, @UserID, NOW(), NULL) ON DUPLICATE KEY UPDATE left_at = NULL", new
                    {
                        RoomID = room.RoomID,
                        UserID = user.UserID
                    }, transaction);

                    await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count WHERE id = @RoomID", new
                    {
                        RoomID = room.RoomID,
                        Count = room.Users.Count
                    }, transaction);

                    await transaction.CommitAsync();
                }
            }
            catch (MySqlException)
            {
                // for now we really don't care about failures in this. it's updating display information each time a user joins/quits and doesn't need to be perfect.
            }
        }

        public async Task AddLoginForUserAsync(int userId, string? userIp)
        {
            if (string.IsNullOrEmpty(userIp))
                return;

            var connection = await getConnectionAsync();

            try
            {
                await connection.ExecuteAsync("INSERT INTO user_login_log (user_id, ip_address, login_method) VALUES (@UserID, @IP, 'spectator')", new
                {
                    UserID = userId,
                    IP = userIp
                });
            }
            catch (MySqlException ex)
            {
                logger.LogWarning(ex, "Could not log login for user {UserId}", userId);
            }
        }

        public async Task RemoveRoomParticipantAsync(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            var connection = await getConnectionAsync();

            try
            {
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    await connection.ExecuteAsync("UPDATE room_participated_users SET left_at = NOW() WHERE room_id = @RoomID AND user_id = @UserID AND left_at IS NULL", new
                    {
                        RoomID = room.RoomID,
                        UserID = user.UserID
                    }, transaction);

                    await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count WHERE id = @RoomID", new
                    {
                        RoomID = room.RoomID,
                        Count = room.Users.Count
                    }, transaction);

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

            return await connection.QuerySingleAsync<multiplayer_playlist_item>("SELECT * FROM playlists WHERE id = @Id AND room_id = @RoomId", new
            {
                Id = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task<long> AddPlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO playlists (owner_id, room_id, beatmap_id, ruleset_id, allowed_mods, required_mods, freestyle, playlist_order, created_at, updated_at)"
                + " VALUES (@owner_id, @room_id, @beatmap_id, @ruleset_id, @allowed_mods, @required_mods, @freestyle, @playlist_order, NOW(), NOW())",
                item);

            return await connection.QuerySingleAsync<long>("SELECT max(id) FROM playlists WHERE room_id = @room_id", item);
        }

        public async Task UpdatePlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "UPDATE playlists SET"
                + " beatmap_id = @beatmap_id,"
                + " ruleset_id = @ruleset_id,"
                + " required_mods = @required_mods,"
                + " allowed_mods = @allowed_mods,"
                + " freestyle = @freestyle,"
                + " playlist_order = @playlist_order,"
                + " updated_at = NOW()"
                + " WHERE id = @id", item);
        }

        public async Task RemovePlaylistItemAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("DELETE FROM playlists WHERE id = @Id AND room_id = @RoomId", new
            {
                Id = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task MarkPlaylistItemAsPlayedAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE playlists SET expired = 1, played_at = NOW(), updated_at = NOW() WHERE id = @PlaylistItemId AND room_id = @RoomId", new
            {
                PlaylistItemId = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task EndMatchAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            // Expire all non-expired items from the playlist.
            // We're not removing them because they may be linked to other tables (e.g. `multiplayer_realtime_room_events`, `multiplayer_scores_high`, etc.)
            await connection.ExecuteAsync(
                "UPDATE playlists p"
                + " SET p.expired = 1, played_at = NOW(), updated_at = NOW()"
                + " WHERE p.room_id = @RoomID"
                + " AND p.expired = 0"
                + " AND (SELECT COUNT(*) FROM multiplayer_score_links l WHERE l.playlist_item_id = p.id) = 0",
                new
                {
                    RoomID = room.RoomID
                });

            int totalUsers = connection.QuerySingle<int>("SELECT COUNT(*) FROM room_participated_users WHERE room_id = @RoomID", new { RoomID = room.RoomID });

            // Close the room.
            await connection.ExecuteAsync("UPDATE rooms SET participant_count = @Count, ends_at = NOW() WHERE id = @RoomID", new
            {
                RoomID = room.RoomID,
                Count = totalUsers,
            });
        }

        public async Task<multiplayer_playlist_item[]> GetAllPlaylistItemsAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<multiplayer_playlist_item>("SELECT * FROM playlists WHERE room_id = @RoomId", new { RoomId = roomId })).ToArray();
        }

        public async Task MarkScoreHasReplay(Score score)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE `scores` SET `has_replay` = 1 WHERE `id` = @scoreId", new
            {
                scoreId = score.ScoreInfo.OnlineID,
            });
        }

        public async Task<SoloScore?> GetScoreFromTokenAsync(long token)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<SoloScore?>(
                "SELECT * FROM `scores` WHERE `id` = (SELECT `score_id` FROM `score_tokens` WHERE `id` = @Id)", new
                {
                    Id = token
                });
        }

        public async Task<SoloScore?> GetScoreAsync(long id)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<SoloScore?>("SELECT * FROM `scores` WHERE `id` = @Id", new
            {
                Id = id
            });
        }

        public Task<bool> IsScoreProcessedAsync(long scoreId)
        {
            // g0v0-server doesn't have score_process_history table
            // For now, assume all scores are not processed to allow processing
            return Task.FromResult(false);
        }

        public async Task<phpbb_zebra?> GetUserRelation(int userId, int zebraId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server uses relationship table instead of phpbb_zebra
            var relationship = await connection.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM `relationship` WHERE `user_id` = @UserId AND `target_id` = @ZebraId", new
            {
                UserId = userId,
                ZebraId = zebraId
            });

            if (relationship == null)
                return null;

            // Convert relationship to phpbb_zebra format for compatibility
            return new phpbb_zebra
            {
                user_id = userId,
                zebra_id = zebraId,
                friend = relationship.type == "Friend",
                foe = relationship.type == "Block"
            };
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
                + "AND u.priv = 1", new
                {
                    UserId = userId
                });
        }

        public async Task<bool> GetUserAllowsPMs(int userId)
        {
            var connection = await getConnectionAsync();

            // 在g0v0-server中，使用pm_friends_only字段（false表示允许所有人发送PM）
            var pmFriendsOnly = await connection.QuerySingleOrDefaultAsync<bool>("SELECT `pm_friends_only` FROM `lazer_users` WHERE `id` = @UserId", new
            {
                UserId = userId
            });
            
            // 如果pm_friends_only为false，表示允许所有人发送PM
            return !pmFriendsOnly;
        }

        public async Task<osu_build?> GetBuildByIdAsync(int buildId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server doesn't have osu_builds table, return a dummy build
            return new osu_build
            {
                build_id = (uint)buildId,
                version = "unknown",
                hash = null,
                users = 0
            };
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
                "SELECT `playlists`.`room_id`, `playlists`.`id` "
                + "FROM `multiplayer_score_links` "
                + "JOIN `playlists` "
                + "ON `multiplayer_score_links`.`playlist_item_id` = `playlists`.`id` "
                + "WHERE `multiplayer_score_links`.`score_id` = @scoreId",
                new { scoreId = scoreId });
        }

        public async Task<IEnumerable<SoloScore>> GetPassingScoresForPlaylistItem(long playlistItemId, ulong afterScoreId = 0)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<SoloScore>(
                "SELECT `scores`.`id`, `scores`.`total_score` FROM `scores` "
                + "JOIN `multiplayer_score_links` ON `multiplayer_score_links`.`score_id` = `scores`.`id` "
                + "JOIN `lazer_users` ON `lazer_users`.`id` = `multiplayer_score_links`.`user_id` "
                + "WHERE `scores`.`passed` = 1 "
                + "AND `multiplayer_score_links`.`playlist_item_id` = @playlistItemId "
                + "AND `multiplayer_score_links`.`score_id` > @afterScoreId "
                + "AND `lazer_users`.`priv` = 1", new
                {
                    playlistItemId = playlistItemId,
                    afterScoreId = afterScoreId,
                }));
        }

        public async Task<multiplayer_scores_high?> GetUserBestScoreAsync(long playlistItemId, int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<multiplayer_scores_high>(
                "SELECT * FROM `multiplayer_scores_high` WHERE `playlist_item_id` = @playlistItemId AND `user_id` = @userId", new
                {
                    playlistItemId = playlistItemId,
                    userId = userId
                });
        }

        public async Task<int> GetUserRankInRoomAsync(long roomId, int userId)
        {
            var connection = await getConnectionAsync();

            // g0v0-server doesn't have multiplayer_rooms_high table
            // For now, return a default rank of 1
            return 1;
        }

        public async Task LogRoomEventAsync(multiplayer_realtime_room_event ev)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO `multiplayer_realtime_room_events` (`room_id`, `event_type`, `playlist_item_id`, `user_id`, `event_detail`, `created_at`, `updated_at`) "
                + "VALUES (@room_id, @event_type, @playlist_item_id, @user_id, @event_detail, NOW(), NOW())",
                ev);
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

            openConnection = new MySqlConnection(connectionString);

            await openConnection.OpenAsync();

            return openConnection;
        }
    }
}
