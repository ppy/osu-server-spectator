// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models
{
    /// <summary>
    /// Enumerates possible modes of queueing new playlist items in a refereed room.
    /// </summary>
    [PublicAPI]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum QueueMode
    {
        /// <summary>
        /// Only the room host and room referees can queue playlist items.
        /// </summary>
        [JsonStringEnumMemberName("host_only")]
        HostOnly = Game.Online.Multiplayer.QueueMode.HostOnly,

        /// <summary>
        /// All players can queue playlist items.
        /// Playlist items are ordered and played on a first-come-first-serve basis.
        /// </summary>
        [JsonStringEnumMemberName("all_players")]
        AllPlayers = Game.Online.Multiplayer.QueueMode.AllPlayers,

        /// <summary>
        /// All players can queue playlist items.
        /// A randomised order is imposed on all players in the room,
        /// and that imposed order determines which player's queued item gets played next.
        /// This is supposed to be fairer than <see cref="AllPlayers"/> in terms of balancing
        /// whose playlist item gets played next, as it is no longer first-come-first-serve.
        /// </summary>
        [JsonStringEnumMemberName("all_players_round_robin")]
        AllPlayersRoundRobin = Game.Online.Multiplayer.QueueMode.AllPlayersRoundRobin,
    }
}
