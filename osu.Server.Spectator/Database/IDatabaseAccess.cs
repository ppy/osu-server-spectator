// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using osu.Game.Online.Metadata;
using osu.Game.Online.Multiplayer;
using osu.Game.Scoring;
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
        /// Returns a username from a <paramref name="userId"/>.
        /// </summary>
        Task<string?> GetUsernameAsync(int userId);

        /// <summary>
        /// Returns the <see cref="multiplayer_room"/> with the given <paramref name="roomId"/>.
        /// </summary>
        Task<multiplayer_room> GetRoomAsync(long roomId);

        /// <summary>
        /// Retrieves a beatmap corresponding to the given <paramref name="beatmapId"/>.
        /// </summary>
        Task<database_beatmap?> GetBeatmapAsync(int beatmapId);

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
        /// Add a new participant for the specified <paramref name="room"/> in the database.
        /// </summary>
        Task AddRoomParticipantAsync(MultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Remove a new participant for the specified <paramref name="room"/> in the database.
        /// </summary>
        Task RemoveRoomParticipantAsync(MultiplayerRoom room, MultiplayerRoomUser user);

        /// <summary>
        /// Retrieves a playlist item from a room.
        /// </summary>
        /// <param name="roomId">The room.</param>
        /// <param name="playlistItemId">The playlist item.</param>
        Task<multiplayer_playlist_item> GetPlaylistItemAsync(long roomId, long playlistItemId);

        /// <summary>
        /// Creates a new playlist item.
        /// </summary>
        /// <returns>The playlist item ID.</returns>
        Task<long> AddPlaylistItemAsync(multiplayer_playlist_item item);

        /// <summary>
        /// Updates an existing playlist item.
        /// </summary>
        /// <param name="item">The new playlist item settings.</param>
        Task UpdatePlaylistItemAsync(multiplayer_playlist_item item);

        /// <summary>
        /// Removes a playlist item.
        /// </summary>
        /// <param name="roomId">The room.</param>
        /// <param name="playlistItemId">The playlist item ID to remove.</param>
        Task RemovePlaylistItemAsync(long roomId, long playlistItemId);

        /// <summary>
        /// Marks a playlist item as having been played.
        /// </summary>
        Task MarkPlaylistItemAsPlayedAsync(long roomId, long playlistItemId);

        /// <summary>
        /// Marks the given <paramref name="room"/> as ended and no longer accepting new players or scores.
        /// </summary>
        Task EndMatchAsync(MultiplayerRoom room);

        /// <summary>
        /// Retrieves all playlist items.
        /// </summary>
        /// <param name="roomId">The room to retrieve playlist items from.</param>
        Task<multiplayer_playlist_item[]> GetAllPlaylistItemsAsync(long roomId);

        /// <summary>
        /// Retrieves any changed beatmap set IDs since last call.
        /// </summary>
        /// <param name="lastQueueId">A queue ID to fetch updated items since</param>
        /// <param name="limit">Maximum number of entries to return. Defaults to 50.</param>
        /// <returns>Update metadata.</returns>
        Task<BeatmapUpdates> GetUpdatedBeatmapSets(int? lastQueueId, int limit = 50);

        /// <summary>
        /// Mark a score as having a replay available.
        /// </summary>
        /// <param name="score">The score to mark.</param>
        Task MarkScoreHasReplay(Score score);

        /// <summary>
        /// Retrieves the <see cref="SoloScore"/> for a given score token. Will return null while the score has not yet been submitted.
        /// </summary>
        /// <param name="token">The score token.</param>
        /// <returns>The <see cref="SoloScore"/>.</returns>
        Task<SoloScore?> GetScoreFromToken(long token);

        /// <summary>
        /// Returns <see langword="true"/> if the score with the supplied <paramref name="scoreId"/> has been successfully processed.
        /// </summary>
        Task<bool> IsScoreProcessedAsync(long scoreId);

        /// <summary>
        /// Returns information about if the user with the supplied <paramref name="zebraId"/> has been added as a friend or blocked by the user with the supplied <paramref name="userId"/>.
        /// </summary>
        Task<phpbb_zebra?> GetUserRelation(int userId, int zebraId);

        /// <summary>
        /// Returns <see langword="true"/> if the user with the supplied <paramref name="userId"/> allows private messages from people not on their friends list.
        /// </summary>
        Task<bool> GetUserAllowsPMs(int userId);

        /// <summary>
        /// Updates the last visit time of users with the supplied <paramref name="userIds"/> to the current time.
        /// </summary>
        Task UpdateLastVisitTimeToNowAsync(IEnumerable<int> userIds);
    }
}
