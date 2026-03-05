// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;

namespace osu.Server.Spectator.Hubs.Referee.Models.Events
{
    /// <summary>
    /// Represents a player in the room.
    /// </summary>
    [PublicAPI]
    public class Player
    {
        /// <summary>
        /// The ID of the player.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// The player's current status.
        /// </summary>
        /// <seealso cref="IRefereeHubClient.UserStatusChanged"/>
        [JsonPropertyName("status")]
        public MatchUserStatus Status { get; set; }

        /// <summary>
        /// The player's current selected style.
        /// Only applicable if the current <see cref="PlaylistItem"/> enabled <see cref="PlaylistItem.Freestyle"/>.
        /// </summary>
        /// <seealso cref="IRefereeHubClient.UserStyleChanged"/>
        [JsonPropertyName("style")]
        public Style Style { get; set; } = new Style();

        /// <summary>
        /// The player's current selected mods.
        /// Only applicable if the current <see cref="PlaylistItem"/> specifies any <see cref="PlaylistItem.AllowedMods"/>.
        /// </summary>
        /// <seealso cref="IRefereeHubClient.UserModsChanged"/>
        [JsonPropertyName("mods")]
        public Mod[] UserMods { get; set; } = [];

        /// <summary>
        /// The player's current team.
        /// Only applicable if the room is in <see cref="MatchType.TeamVersus"/>.
        /// </summary>
        /// <seealso cref="IRefereeHubClient.UserTeamChanged"/>
        [JsonPropertyName("team")]
        public MatchTeam? Team { get; set; }

        [Newtonsoft.Json.JsonConstructor]
        public Player()
        {
        }

        internal Player(MultiplayerRoomUser user)
        {
            if (user.Role != MultiplayerRoomUserRole.Player)
                throw new ArgumentException(nameof(user));

            UserId = user.UserID;

            switch (user.State)
            {
                case MultiplayerUserState.Idle:
                default:
                    Status = MatchUserStatus.Idle;
                    break;

                case MultiplayerUserState.Ready:
                case MultiplayerUserState.WaitingForLoad:
                case MultiplayerUserState.Loaded:
                case MultiplayerUserState.ReadyForGameplay:
                    Status = MatchUserStatus.Ready;
                    break;

                case MultiplayerUserState.Playing:
                    Status = MatchUserStatus.Playing;
                    break;

                case MultiplayerUserState.FinishedPlay:
                case MultiplayerUserState.Results:
                    Status = MatchUserStatus.FinishedPlay;
                    break;

                case MultiplayerUserState.Spectating:
                    Status = MatchUserStatus.Spectating;
                    break;
            }

            Style = new Style
            {
                RulesetId = user.RulesetId,
                BeatmapId = user.BeatmapId,
            };
            UserMods = user.Mods.Select(Mod.FromAPIMod).ToArray();

            if (user.MatchState is TeamVersusUserState teamVersusUserState)
                Team = (MatchTeam)teamVersusUserState.TeamID;
        }
    }

    public class Style
    {
        /// <summary>
        /// The ID of the ruleset selected by the user.
        /// </summary>
        [JsonPropertyName("ruleset_id")]
        public int? RulesetId { get; set; }

        /// <summary>
        /// The ID of the beatmap difficulty selected by the user.
        /// </summary>
        [JsonPropertyName("beatmap_id")]
        public int? BeatmapId { get; set; }
    }
}
