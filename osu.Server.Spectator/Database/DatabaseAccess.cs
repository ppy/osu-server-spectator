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

            return await connection.QueryFirstOrDefaultAsync<int?>("SELECT user_id FROM oauth_access_tokens WHERE revoked = false AND expires_at > now() AND id = @id",
                new { id = jwtToken.Id });
        }

        public async Task<string?> GetUsernameAsync(int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<string?>("SELECT username FROM phpbb_users WHERE user_id = @UserID", new
            {
                UserID = userId
            });
        }

        public async Task<bool> IsUserRestrictedAsync(int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<byte>("SELECT user_warnings FROM phpbb_users WHERE user_id = @UserID", new
            {
                UserID = userId
            }) != 0;
        }

        public async Task<multiplayer_room?> GetRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM multiplayer_rooms WHERE id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public async Task<multiplayer_room?> GetRealtimeRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM multiplayer_rooms WHERE type != 'playlists' AND id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public async Task<database_beatmap?> GetBeatmapAsync(int beatmapId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<database_beatmap>(
                "SELECT beatmap_id, beatmapset_id, checksum, approved, difficultyrating, playmode, osu_file_version FROM osu_beatmaps WHERE beatmap_id = @BeatmapId AND deleted_at IS NULL", new
                {
                    BeatmapId = beatmapId
                });
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
                "SELECT beatmap_id, beatmapset_id, checksum, approved, difficultyrating, playmode, osu_file_version FROM osu_beatmaps WHERE beatmapset_id = @BeatmapSetId AND deleted_at IS NULL", new
                {
                    BeatmapSetId = beatmapSetId
                })).ToArray();
        }

        public async Task MarkRoomActiveAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET ends_at = null WHERE id = @RoomID", new
            {
                RoomID = room.RoomID
            });
        }

        public async Task UpdateRoomSettingsAsync(MultiplayerRoom room)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET name = @Name, password = @Password, type = @MatchType, queue_mode = @QueueMode WHERE id = @RoomID", new
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

            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET status = @Status WHERE id = @RoomID", new
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
                await connection.ExecuteAsync("UPDATE multiplayer_rooms SET user_id = @HostUserID WHERE id = @RoomID", new
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
                    await connection.ExecuteAsync("INSERT INTO multiplayer_rooms_high (room_id, user_id, in_room) VALUES (@RoomID, @UserID, 1) ON DUPLICATE KEY UPDATE in_room = 1", new
                    {
                        RoomID = room.RoomID,
                        UserID = user.UserID
                    }, transaction);

                    await connection.ExecuteAsync("UPDATE multiplayer_rooms SET participant_count = @Count WHERE id = @RoomID", new
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
                await connection.ExecuteAsync("INSERT INTO osu_logins (user_id, ip) VALUES (@UserID, @IP)", new
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
                    await connection.ExecuteAsync("UPDATE multiplayer_rooms_high SET in_room = 0 WHERE room_id = @RoomID AND user_id = @UserID", new
                    {
                        RoomID = room.RoomID,
                        UserID = user.UserID
                    }, transaction);

                    await connection.ExecuteAsync("UPDATE multiplayer_rooms SET participant_count = @Count WHERE id = @RoomID", new
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

            return await connection.QuerySingleAsync<multiplayer_playlist_item>("SELECT `i`.*, `b`.`checksum`, `b`.`difficultyrating` "
                                                                                + "FROM `multiplayer_playlist_items` `i` "
                                                                                + "JOIN `osu_beatmaps` `b` "
                                                                                + "ON `b`.`beatmap_id` = `i`.`beatmap_id` "
                                                                                + "WHERE `i`.`id` = @Id "
                                                                                + "AND `i`.`room_id` = @RoomId", new
            {
                Id = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task<long> AddPlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO multiplayer_playlist_items (owner_id, room_id, beatmap_id, ruleset_id, allowed_mods, required_mods, freestyle, playlist_order, created_at, updated_at)"
                + " VALUES (@owner_id, @room_id, @beatmap_id, @ruleset_id, @allowed_mods, @required_mods, @freestyle, @playlist_order, NOW(), NOW())",
                item);

            return await connection.QuerySingleAsync<long>("SELECT max(id) FROM multiplayer_playlist_items WHERE room_id = @room_id", item);
        }

        public async Task UpdatePlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "UPDATE multiplayer_playlist_items SET"
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

            await connection.ExecuteAsync("DELETE FROM multiplayer_playlist_items WHERE id = @Id AND room_id = @RoomId", new
            {
                Id = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task MarkPlaylistItemAsPlayedAsync(long roomId, long playlistItemId)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE multiplayer_playlist_items SET expired = 1, played_at = NOW(), updated_at = NOW() WHERE id = @PlaylistItemId AND room_id = @RoomId", new
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
                "UPDATE multiplayer_playlist_items p"
                + " SET p.expired = 1, played_at = NOW(), updated_at = NOW()"
                + " WHERE p.room_id = @RoomID"
                + " AND p.expired = 0"
                + " AND (SELECT COUNT(*) FROM multiplayer_score_links l WHERE l.playlist_item_id = p.id) = 0",
                new
                {
                    RoomID = room.RoomID
                });

            int totalUsers = connection.QuerySingle<int>("SELECT COUNT(*) FROM multiplayer_rooms_high WHERE room_id = @RoomID", new { RoomID = room.RoomID });

            // Close the room.
            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET participant_count = @Count, ends_at = NOW() WHERE id = @RoomID", new
            {
                RoomID = room.RoomID,
                Count = totalUsers,
            });
        }

        public async Task<multiplayer_playlist_item[]> GetAllPlaylistItemsAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<multiplayer_playlist_item>("SELECT `i`.*, `b`.`checksum`, `b`.`difficultyrating` "
                                                                           + "FROM `multiplayer_playlist_items` `i` "
                                                                           + "JOIN `osu_beatmaps` `b` "
                                                                           + "ON `b`.`beatmap_id` = `i`.`beatmap_id` "
                                                                           + "WHERE `i`.`room_id` = @RoomId", new
            {
                RoomId = roomId
            })).ToArray();
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

        public async Task<bool> IsScoreProcessedAsync(long scoreId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<bool>("SELECT 1 FROM `score_process_history` WHERE `score_id` = @ScoreId", new
            {
                ScoreId = scoreId
            });
        }

        public async Task<phpbb_zebra?> GetUserRelation(int userId, int zebraId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<phpbb_zebra?>("SELECT * FROM `phpbb_zebra` WHERE `user_id` = @UserId AND `zebra_id` = @ZebraId", new
            {
                UserId = userId,
                ZebraId = zebraId
            });
        }

        public async Task<IEnumerable<int>> GetUserFriendsAsync(int userId)
        {
            var connection = await getConnectionAsync();

            // Query pulled from osu!bancho.
            return await connection.QueryAsync<int>(
                "SELECT zebra_id FROM phpbb_zebra z "
                + "JOIN phpbb_users u ON z.zebra_id = u.user_id "
                + "WHERE z.user_id = @UserId "
                + "AND friend = 1 "
                + "AND (`user_warnings` = '0' and `user_type` = '0')", new
                {
                    UserId = userId
                });
        }

        public async Task<bool> GetUserAllowsPMs(int userId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<bool>("SELECT `user_allow_pm` FROM `phpbb_users` WHERE `user_id` = @UserId", new
            {
                UserId = userId
            });
        }

        public async Task<osu_build?> GetBuildByIdAsync(int buildId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleAsync<osu_build?>("SELECT `build_id`, `version`, `hash`, `users` FROM `osu_builds` WHERE `build_id` = @BuildId",
                new
                {
                    BuildId = buildId
                });
        }

        public async Task<IEnumerable<osu_build>> GetAllMainLazerBuildsAsync()
        {
            var connection = await getConnectionAsync();

            return await connection.QueryAsync<osu_build>(
                "SELECT `build_id`, `version`, `hash`, `users` "
                + "FROM `osu_builds` "
                + "WHERE stream_id IN (7, 17) AND allow_bancho = 1");
        }

        public async Task<IEnumerable<osu_build>> GetAllPlatformSpecificLazerBuildsAsync()
        {
            var connection = await getConnectionAsync();

            return await connection.QueryAsync<osu_build>(
                "SELECT `build_id`, `version`, `hash`, `users` "
                + "FROM `osu_builds` "
                // Should match checks in BuildUserCountUpdater.build_version_regex.
                + "WHERE `stream_id` IS NULL AND (`version` LIKE '%-lazer-%' OR `version` LIKE '%-tachyon-%') AND `allow_bancho` = 1");
        }

        public async Task UpdateBuildUserCountAsync(osu_build build)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE `osu_builds` SET `users` = @users WHERE `build_id` = @build_id", build);
        }

        public async Task<IEnumerable<chat_filter>> GetAllChatFiltersAsync()
        {
            var connection = await getConnectionAsync();

            return await connection.QueryAsync<chat_filter>("SELECT * FROM `chat_filters`");
        }

        public async Task<IEnumerable<multiplayer_room>> GetActiveDailyChallengeRoomsAsync()
        {
            var connection = await getConnectionAsync();

            return await connection.QueryAsync<multiplayer_room>(
                "SELECT * FROM `multiplayer_rooms` "
                + "WHERE `category` = 'daily_challenge' "
                + "AND `type` = 'playlists' "
                + "AND `starts_at` <= NOW() "
                + "AND `ends_at` > NOW()");
        }

        public async Task<(long roomID, long playlistItemID)?> GetMultiplayerRoomIdForScoreAsync(long scoreId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<(long, long)?>(
                "SELECT `multiplayer_playlist_items`.`room_id`, `multiplayer_playlist_items`.`id` "
                + "FROM `multiplayer_score_links` "
                + "JOIN `multiplayer_playlist_items` "
                + "ON `multiplayer_score_links`.`playlist_item_id` = `multiplayer_playlist_items`.`id` "
                + "WHERE `multiplayer_score_links`.`score_id` = @scoreId",
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
        /// Retrieves the passing score ids and total scores on a playlist item.
        /// </summary>
        /// <param name="playlistItemId">The playlist item.</param>
        /// <param name="afterScoreId">The score ID after which to retrieve.</param>
        public async Task<IEnumerable<SoloScore>> GetPassingScoresForPlaylistItem(long playlistItemId, ulong afterScoreId = 0)
        {
            var connection = await getConnectionAsync();

            return (await connection.QueryAsync<SoloScore>(
                "SELECT `scores`.`id`, `scores`.`total_score` FROM `scores` "
                + "JOIN `multiplayer_score_links` ON `multiplayer_score_links`.`score_id` = `scores`.`id` "
                + "JOIN `phpbb_users` ON `phpbb_users`.`user_id` = `multiplayer_score_links`.`user_id` "
                + "WHERE `scores`.`passed` = 1 "
                + "AND `multiplayer_score_links`.`playlist_item_id` = @playlistItemId "
                + "AND `multiplayer_score_links`.`score_id` > @afterScoreId "
                + "AND `phpbb_users`.`user_type` = 0 "
                + "AND `phpbb_users`.`user_warnings` = 0", new
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

            return await connection.QuerySingleAsync<int>(
                "WITH `user_score` AS (SELECT `total_score`, `last_score_id` FROM `multiplayer_rooms_high` WHERE `room_id` = @roomId AND `user_id` = @userId) "
                + "SELECT COUNT(1) + 1 FROM `multiplayer_rooms_high` "
                + "JOIN `phpbb_users` ON `phpbb_users`.`user_id` = `multiplayer_rooms_high`.`user_id` "
                + "WHERE `multiplayer_rooms_high`.`room_id` = @roomId "
                + "AND `multiplayer_rooms_high`.`user_id` != @userId "
                + "AND `phpbb_users`.`user_type` = 0 "
                + "AND `phpbb_users`.`user_warnings` = 0 "
                + "AND (`multiplayer_rooms_high`.`total_score` > (SELECT `total_score` FROM `user_score`) OR "
                + "(`multiplayer_rooms_high`.`total_score` = (SELECT `total_score` FROM `user_score`) AND `multiplayer_rooms_high`.`last_score_id` < (SELECT `last_score_id` FROM `user_score`)))",
                new
                {
                    roomId = roomId,
                    userId = userId,
                });
        }

        public async Task LogRoomEventAsync(multiplayer_realtime_room_event ev)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO `multiplayer_realtime_room_events` (`room_id`, `event_type`, `playlist_item_id`, `user_id`, `event_detail`, `created_at`, `updated_at`) "
                + "VALUES (@room_id, @event_type, @playlist_item_id, @user_id, @event_detail, NOW(), NOW())",
                ev);
        }

        public async Task ToggleUserPresenceAsync(int userId, bool visible)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "UPDATE `phpbb_users` SET `user_allow_viewonline` = @visible WHERE `user_id` = @userId",
                new
                {
                    visible = visible,
                    userId = userId
                });
        }

        public async Task<float> GetUserPPAsync(int userId, int rulesetId)
        {
            string statsTable = rulesetId switch
            {
                0 => "osu_user_stats",
                1 => "osu_user_stats_taiko",
                2 => "osu_user_stats_fruits",
                3 => "osu_user_stats_mania",
                _ => throw new ArgumentOutOfRangeException(nameof(rulesetId), rulesetId, null)
            };

            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<float>($"SELECT `rank_score` FROM {statsTable} WHERE `user_id` = @userId", new
            {
                userId = userId
            });
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

            return (await connection.QueryAsync<matchmaking_pool_beatmap>("SELECT p.*, b.checksum, b.difficultyrating FROM `matchmaking_pool_beatmaps` p "
                                                                          + "JOIN `osu_beatmaps` b ON p.beatmap_id = b.beatmap_id "
                                                                          + "WHERE p.pool_id = @PoolId", new
            {
                PoolId = poolId
            })).ToArray();
        }

        public async Task IncrementMatchmakingSelectionCount(matchmaking_pool_beatmap[] beatmaps)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync("UPDATE matchmaking_pool_beatmaps "
                                          + "SET selection_count = selection_count + 1 "
                                          + "WHERE id IN @ItemIDs", new
            {
                ItemIDs = beatmaps.Select(b => b.id).ToArray()
            });
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

            await connection.ExecuteAsync("INSERT INTO `matchmaking_user_stats` (`user_id`, `ruleset_id`, `first_placements`, `total_points`, `rating`, `elo_data`, `created_at`, `updated_at`) "
                                          + "VALUES (@UserId, @RulesetId, @FirstPlacements, @TotalPoints, @Rating, @EloData, NOW(), NOW()) "
                                          + "ON DUPLICATE KEY UPDATE "
                                          + "`first_placements` = @FirstPlacements, "
                                          + "`total_points` = @TotalPoints, "
                                          + "`rating` = @Rating, "
                                          + "`elo_data` = @EloData, "
                                          + "`updated_at` = NOW()", new
            {
                UserId = stats.user_id,
                RulesetId = stats.ruleset_id,
                FirstPlacements = stats.first_placements,
                TotalPoints = stats.total_points,
                Rating = Math.Round(stats.EloData.ApproximatePosterior.Mu),
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

            openConnection = new MySqlConnection(
                $"Server={AppSettings.DatabaseHost};Port={AppSettings.DatabasePort};Database=osu;User ID={AppSettings.DatabaseUser};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;Pipelining=false");

            await openConnection.OpenAsync();

            return openConnection;
        }
    }
}
