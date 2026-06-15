// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Framework.Extensions.TypeExtensions;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Hubs.Referee.Models.Requests;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// Contains the current state of the room.
    /// </summary>
    [PublicAPI]
    public class MatchState
    {
        /// <summary>
        /// The current match type.
        /// </summary>
        [JsonPropertyName("type")]
        public MatchType Type { get; }

        /// <summary>
        /// Whether the room is currently locked.
        /// When the room is locked, players cannot change teams themselves, only referees can change it for them.
        /// </summary>
        [JsonPropertyName("locked")]
        public bool Locked { get; }

        /// <summary>
        /// <para>
        /// The current state of slots in the room.
        /// </para>
        /// <para>
        /// If the whole array is <see langword="null"/>, then slots are not active in the room
        /// (there is no limit to number of participants).
        /// </para>
        /// <para>
        /// If the array is not <see langword="null"/>:
        /// <list type="bullet">
        /// <item>The length of the array is equal to the number of slots in the room.</item>
        /// <item>If a slot is empty, its corresponding entry in the array will be <see langword="null"/></item>.
        /// <item>If a slot is not empty, its corresponding entry in the array will be the ID of the user occupying it.</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <seealso cref="MakeRoomRequest.MaxParticipants"/>
        /// <seealso cref="RoomSettingsChangedEvent.MaxParticipants"/>
        [JsonPropertyName("slots")]
        public int?[]? Slots { get; }

        protected MatchState(MatchType type, bool locked, int?[]? slots)
        {
            Type = type;
            Locked = locked;
            Slots = slots;
        }

        internal static MatchState Create(MultiplayerRoom room)
        {
            switch (room.MatchState)
            {
                case StandardMatchRoomState standardRoomState:
                    return new MatchState((MatchType)room.Settings.MatchType, standardRoomState.Locked, standardRoomState.Slots);

                case null:
                    return new MatchState((MatchType)room.Settings.MatchType, false, null);

                default:
                    throw new ArgumentOutOfRangeException(nameof(room.MatchState), room.MatchState, $"Cannot handle room state of type {room.MatchState.GetType().ReadableName()}");
            }
        }
    }
}
