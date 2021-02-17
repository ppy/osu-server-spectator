// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Database
{
    public class DatabaseAccess : IDatabaseAccess
    {
        private readonly MySqlConnection connection;

        public DatabaseAccess()
        {
            connection = getConnection();
        }

        private static MySqlConnection getConnection()
        {
            string host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
            string user = Environment.GetEnvironmentVariable("DB_USER") ?? "root";

            DapperExtensions.InstallDateTimeOffsetMapper();

            var connection = new MySqlConnection($"Server={host};Database=osu;User ID={user};ConnectionTimeout=5;ConnectionReset=false;Pooling=true;");
            connection.Open();
            return connection;
        }

        public Task<int?> GetUserIdFromTokenAsync(JwtSecurityToken jwtToken)
        {
            return connection.QueryFirstOrDefaultAsync<int?>("SELECT user_id FROM oauth_access_tokens WHERE revoked = false AND expires_at > now() AND id = @id",
                new { id = jwtToken.Id });
        }

        public async Task<bool> IsUserRestrictedAsync(int userId)
        {
            return await connection.QueryFirstOrDefaultAsync<byte>("SELECT user_warnings FROM phpbb_users WHERE user_id = @UserID", new
            {
                UserID = userId
            }) != 0;
        }

        public Task<multiplayer_room> GetRoomAsync(long roomId)
        {
            return connection.QueryFirstOrDefaultAsync<multiplayer_room>("SELECT * FROM multiplayer_rooms WHERE category = 'realtime' AND id = @RoomID", new
            {
                RoomID = roomId
            });
        }

        public Task<multiplayer_playlist_item> GetCurrentPlaylistItemAsync(long roomId)
        {
            return connection.QuerySingleAsync<multiplayer_playlist_item>(
                "SELECT * FROM multiplayer_playlist_items WHERE id = (SELECT MAX(id) FROM multiplayer_playlist_items WHERE room_id = @RoomId)", new
                {
                    RoomID = roomId
                });
        }

        public Task<string> GetBeatmapChecksumAsync(int beatmapId)
        {
            return connection.QuerySingleAsync<string>("SELECT checksum from osu_beatmaps where beatmap_id = @BeatmapID", new
            {
                BeatmapId = beatmapId
            });
        }

        public Task MarkRoomActiveAsync(MultiplayerRoom room)
        {
            return connection.ExecuteAsync("UPDATE multiplayer_rooms SET ends_at = null WHERE id = @RoomID", new
            {
                RoomID = room.RoomID
            });
        }

        public async Task UpdateRoomSettingsAsync(MultiplayerRoom room)
        {
            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET name = @Name WHERE id = @RoomID", new
            {
                RoomID = room.RoomID,
                Name = room.Settings.Name
            });

            var currentItem = await GetCurrentPlaylistItemAsync(room.RoomID);
            var newItem = new multiplayer_playlist_item(room) { id = currentItem.id };

            await connection.ExecuteAsync("UPDATE multiplayer_playlist_items SET "
                                          + " beatmap_id = @beatmap_id,"
                                          + " ruleset_id = @ruleset_id,"
                                          + " required_mods = @required_mods,"
                                          + " allowed_mods = @allowed_mods,"
                                          + " updated_at = NOW()"
                                          + " WHERE id = @id", newItem);
        }

        public async Task UpdateRoomHostAsync(MultiplayerRoom room)
        {
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

        public async Task UpdateRoomParticipantsAsync(MultiplayerRoom room)
        {
            try
            {
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    // This should be considered *very* temporary, and for display purposes only!
                    await connection.ExecuteAsync("DELETE FROM multiplayer_rooms_high WHERE room_id = @RoomID", new
                    {
                        RoomID = room.RoomID
                    }, transaction);

                    foreach (var u in room.Users)
                    {
                        await connection.ExecuteAsync("INSERT INTO multiplayer_rooms_high (room_id, user_id) VALUES (@RoomID, @UserID)", new
                        {
                            RoomID = room.RoomID,
                            UserID = u.UserID
                        }, transaction);
                    }

                    await transaction.CommitAsync();
                }

                await connection.ExecuteAsync("UPDATE multiplayer_rooms SET participant_count = @Count WHERE id = @RoomID", new
                {
                    RoomID = room.RoomID,
                    Count = room.Users.Count
                });
            }
            catch (MySqlException)
            {
                // for now we really don't care about failures in this. it's updating display information each time a user joins/quits and doesn't need to be perfect.
            }
        }

        public async Task<long> CommitPlaylistItem(MultiplayerRoom room)
        {
            var currentItem = await GetCurrentPlaylistItemAsync(room.RoomID);

            await connection.ExecuteAsync("UPDATE multiplayer_playlist_items SET played = 1, updated_at = NOW() WHERE id=@id", currentItem);
            await connection.ExecuteAsync(
                "INSERT INTO multiplayer_playlist_items (room_id, beatmap_id, ruleset_id, allowed_mods, required_mods, created_at, updated_at)"
                + " VALUES (@room_id, @beatmap_id, @ruleset_id, @allowed_mods, @required_mods, NOW(), NOW())",
                currentItem);

            return (await GetCurrentPlaylistItemAsync(room.RoomID)).id;
        }

        public async Task EndMatchAsync(MultiplayerRoom room)
        {
            // Remove all non-played items from the playlist. For now this is only the current item.
            var currentItem = await GetCurrentPlaylistItemAsync(room.RoomID);
            await connection.ExecuteAsync("DELETE FROM multiplayer_playlist_items WHERE id=@id", currentItem);

            // Close the room.
            await connection.ExecuteAsync("UPDATE multiplayer_rooms SET ends_at = NOW() WHERE id = @RoomID", new
            {
                RoomID = room.RoomID
            });
        }

        public void Dispose()
        {
            connection.Dispose();
        }
    }
}
