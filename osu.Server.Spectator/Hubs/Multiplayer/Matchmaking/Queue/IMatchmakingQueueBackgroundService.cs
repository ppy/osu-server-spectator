// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public interface IMatchmakingQueueBackgroundService : IHostedService
    {
        /// <summary>
        /// Records the current state of a match.
        /// </summary>
        Task RecordMatch(int poolId, MatchRoomState state);

        /// <summary>
        /// Records the result of a beatmap, adjusting the beatmap's rating as appropriate.
        /// </summary>
        /// <param name="poolId">The pool on which the beatmap was played.</param>
        /// <param name="beatmapId">The beatmap that was played.</param>
        /// <param name="mods">Any mods the beatmap was played with.</param>
        /// <param name="scores">The achieved scores.</param>
        /// <param name="ratings">The ratings corresponding to each of the scores.</param>
        Task RecordBeatmapResult(uint poolId, int beatmapId, APIMod[] mods, int[] scores, EloRating[] ratings);

        /// <summary>
        /// Whether a user is in the matchmaking queue.
        /// </summary>
        bool IsInQueue(MultiplayerClientState state);

        /// <summary>
        /// Adds a user to the matchmaking lobby.
        /// </summary>
        Task AddToLobbyAsync(MultiplayerClientState state, int poolId);

        /// <summary>
        /// Remove sa user from the matchmaking lobby.
        /// </summary>
        Task RemoveFromLobbyAsync(MultiplayerClientState state);

        /// <summary>
        /// Adds a user to the matchmaking queue.
        /// </summary>
        Task AddToQueueAsync(MultiplayerClientState state, int poolId);

        /// <summary>
        /// Removes a user from the matchmaking queue.
        /// </summary>
        Task RemoveFromQueueAsync(MultiplayerClientState state);

        /// <summary>
        /// User accepts an invitation.
        /// </summary>
        Task AcceptInvitationAsync(MultiplayerClientState state);

        /// <summary>
        /// User declines an invitation.
        /// </summary>
        Task DeclineInvitationAsync(MultiplayerClientState state);
    }
}
