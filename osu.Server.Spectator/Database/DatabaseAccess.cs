// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Database
{
    public class DatabaseAccess : IDatabaseAccess
    {
        private MySqlConnection? openConnection;

        public async Task<int?> GetUserIdFromTokenAsync(JwtSecurityToken jwtToken)
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

        public async Task<multiplayer_room> GetRoomAsync(long roomId)
        {
            var connection = await getConnectionAsync();

            return await connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM multiplayer_rooms WHERE type != 'playlists' AND id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public async Task<string?> GetBeatmapChecksumAsync(int beatmapId)
        {
            var connection = await getConnectionAsync();

            return await connection.QuerySingleOrDefaultAsync<string?>("SELECT checksum from osu_beatmaps WHERE beatmap_id = @BeatmapID AND deleted_at IS NULL", new
            {
                BeatmapId = beatmapId
            });
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

            return await connection.QuerySingleAsync<multiplayer_playlist_item>("SELECT * FROM multiplayer_playlist_items WHERE id = @Id AND room_id = @RoomId", new
            {
                Id = playlistItemId,
                RoomId = roomId
            });
        }

        public async Task<long> AddPlaylistItemAsync(multiplayer_playlist_item item)
        {
            var connection = await getConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO multiplayer_playlist_items (owner_id, room_id, beatmap_id, ruleset_id, allowed_mods, required_mods, playlist_order, created_at, updated_at)"
                + " VALUES (@owner_id, @room_id, @beatmap_id, @ruleset_id, @allowed_mods, @required_mods, @playlist_order, NOW(), NOW())",
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

            // Remove all non-expired items from the playlist as they have no scores.
            await connection.ExecuteAsync(
                "DELETE FROM multiplayer_playlist_items p"
                + " WHERE p.room_id = @RoomID"
                + " AND p.expired = 0"
                + " AND (SELECT COUNT(*) FROM multiplayer_scores s WHERE s.playlist_item_id = p.id) = 0",
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

            return (await connection.QueryAsync<multiplayer_playlist_item>("SELECT * FROM multiplayer_playlist_items WHERE room_id = @RoomId", new { RoomId = roomId })).ToArray();
        }

        public async Task<(int[] beatmapSetIds, uint lastQueueId)> GetUpdatedBeatmapSets(uint? lastQueueId)
        {
            var connection = await getConnectionAsync();

            if (lastQueueId.HasValue)
            {
                var items = (await connection.QueryAsync<bss_process_queue_item>("SELECT * FROM bss_process_queue WHERE status = 2 AND queue_id > @lastQueueId", new { lastQueueId })).ToArray();

                return (items.Select(i => i.beatmapset_id).ToArray(), items.LastOrDefault()?.queue_id ?? lastQueueId.Value);
            }

            var lastEntry = await connection.QueryFirstAsync<bss_process_queue_item>("SELECT * FROM bss_process_queue WHERE status = 2 ORDER BY queue_id DESC LIMIT 1");

            return (Array.Empty<int>(), lastEntry?.queue_id ?? 0);
        }

        public void Dispose()
        {
            openConnection?.Dispose();
        }

        private async Task<MySqlConnection> getConnectionAsync()
        {
            if (openConnection != null)
                return openConnection;

            string host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
            string user = Environment.GetEnvironmentVariable("DB_USER") ?? "root";
            string port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";

            DapperExtensions.InstallDateTimeOffsetMapper();

            openConnection = new MySqlConnection($"Server={host};Port={port};Database=osu;User ID={user};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;");

            await openConnection.OpenAsync();

            return openConnection;
        }
    }
}
