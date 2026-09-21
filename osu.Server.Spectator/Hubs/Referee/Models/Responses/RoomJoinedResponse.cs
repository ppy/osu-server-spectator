// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Framework.Extensions.TypeExtensions;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs.Referee.Models.Events;
using osu.Server.Spectator.Hubs.Referee.Models.Requests;

namespace osu.Server.Spectator.Hubs.Referee.Models.Responses
{
    /// <summary>
    /// Contains information about a room that was just joined.
    /// </summary>
    [PublicAPI]
    public class RoomJoinedResponse
    {
        /// <summary>
        /// The ID of the room.
        /// </summary>
        [JsonPropertyName("room_id")]
        public long RoomId { get; set; }

        /// <summary>
        /// The ID of the chat channel for the joined multiplayer room.
        /// </summary>
        [JsonPropertyName("chat_channel_id")]
        public int ChatChannelId { get; set; }

        /// <summary>
        /// The name of the room.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The password to the room.
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The maximum number of players in the room.
        /// <list type="bullet">
        /// <item>If 0 or missing, the room will allow an unlimited number of participants, but will not have enabled player slots.</item>
        /// <item>If in the range [2, <see cref="MakeRoomRequest.MAX_PARTICIPANTS_LIMIT"/>] inclusive, the room will have the given number of slots to be occupied by participants.</item>
        /// </list>
        /// </summary>
        [JsonPropertyName("max_participants")]
        public byte MaxParticipants { get; set; }

        /// <summary>
        /// The state of the room.
        /// Includes the <see cref="MatchType"/>, as well as any additional information specific to that match type.
        /// </summary>
        public MatchState State { get; set; } = null!;

        [JsonPropertyName("playlist")]
        public PlaylistItem[] Playlist { get; set; } = [];

        [JsonPropertyName("players")]
        public Player[] Players { get; set; } = [];

        [JsonPropertyName("referees")]
        public Events.Referee[] Referees { get; set; } = [];

        [JsonConstructor]
        public RoomJoinedResponse()
        {
        }

        internal RoomJoinedResponse(MultiplayerRoom room)
        {
            RoomId = room.RoomID;
            ChatChannelId = room.ChannelID;
            Name = room.Settings.Name;
            Password = room.Settings.Password;
            MaxParticipants = room.Settings.MaxParticipants ?? 0;
            State = MatchState.Create(room) ?? throw new InvalidOperationException($"Could not create room state (ID: {room.RoomID}, state type: {room.MatchState?.GetType().ReadableName()})");
            Playlist = room.Playlist.Select(item => new PlaylistItem(item)).ToArray();
            Players = room.Users.Where(u => u.Role == MultiplayerRoomUserRole.Player).Select(u => new Player(u)).ToArray();
            Referees = room.Users.Where(u => u.Role == MultiplayerRoomUserRole.Referee).Select(u => new Events.Referee(u)).ToArray();
        }
    }
}
