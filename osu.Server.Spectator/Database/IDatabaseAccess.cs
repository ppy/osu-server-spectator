// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Database
{
    public interface IDatabaseAccess : IDisposable
    {
        /// <summary>
        /// Returns the database ID of the user to whom the supplied <paramref name="jwtToken"/> belongs.
        /// Will be <c>null</c> if the token does not exist, has expired or has been revoked.
        /// </summary>
        Task<int?> GetUserIdFromTokenAsync(JwtSecurityToken jwtToken);

        /// <summary>
        /// Whether the user with the given <paramref name="userId"/> is currently restricted.
        /// </summary>
        Task<bool> IsUserRestrictedAsync(int userId);

        /// <summary>
        /// Returns the <see cref="multiplayer_room"/> with the given <paramref name="roomId"/>.
        /// </summary>
        Task<multiplayer_room> GetRoomAsync(long roomId);

        /// <summary>
        /// Returns the single current <see cref="multiplayer_playlist_item"/> of a multiplayer room with ID <paramref name="roomId"/>.
        /// This method is not valid for use with playlists "rooms".
        /// </summary>
        Task<multiplayer_playlist_item> GetCurrentPlaylistItemAsync(long roomId);

        /// <summary>
        /// Returns the checksum of the beatmap with the given <paramref name="beatmapId"/>.
        /// </summary>
        Task<string> GetBeatmapChecksumAsync(int beatmapId);

        /// <summary>
        /// Marks the given <paramref name="room"/> as active and accepting new players.
        /// </summary>
        Task MarkRoomActiveAsync(MultiplayerRoom room);

        /// <summary>
        /// Updates the current settings of <paramref name="room"/> in the database.
        /// </summary>
        Task UpdateRoomSettingsAsync(MultiplayerRoom room);

        /// <summary>
        /// Updates the current host of <paramref name="room"/> in the database.
        /// </summary>
        Task UpdateRoomHostAsync(MultiplayerRoom room);

        /// <summary>
        /// Updates the current participants of <paramref name="room"/> in the database.
        /// </summary>
        Task UpdateRoomParticipantsAsync(MultiplayerRoom room);

        /// <summary>
        /// Commits a playlist item, making it immutable in the database.
        /// A new item is inserted with the existing settings for any new modifications.
        /// </summary>
        Task CommitPlaylistItem(MultiplayerRoom room);

        /// <summary>
        /// Marks the given <paramref name="room"/> as ended and no longer accepting new players or scores.
        /// </summary>
        Task EndMatchAsync(MultiplayerRoom room);
    }
}
