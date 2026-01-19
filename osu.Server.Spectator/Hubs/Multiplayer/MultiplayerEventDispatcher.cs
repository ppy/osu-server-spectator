// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using osu.Game.Online.API;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class MultiplayerEventDispatcher
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly IHubContext<MultiplayerHub> multiplayerHubContext;
        private readonly ILogger<MultiplayerEventDispatcher> logger;

        public MultiplayerEventDispatcher(
            IDatabaseFactory databaseFactory,
            IHubContext<MultiplayerHub> multiplayerHubContext,
            ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.multiplayerHubContext = multiplayerHubContext;
            logger = loggerFactory.CreateLogger<MultiplayerEventDispatcher>();
        }

        /// <summary>
        /// Subscribes a connection with the given <paramref name="connectionId"/>
        /// to multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task SubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.AddToGroupAsync(connectionId, GetGroupId(roomId));
        }

        /// <summary>
        /// Unsubscribes a connection with the given <paramref name="connectionId"/>
        /// from multiplayer events relevant to active players
        /// which occur in the room with the given <paramref name="roomId"/>.
        /// </summary>
        public async Task UnsubscribePlayerAsync(long roomId, string connectionId)
        {
            await multiplayerHubContext.Groups.RemoveFromGroupAsync(connectionId, GetGroupId(roomId));
        }

        /// <summary>
        /// A new multiplayer room was created.
        /// </summary>
        /// <param name="roomId">The ID of the created room.</param>
        /// <param name="userId">The ID of the user that created the room.</param>
        public async Task OnRoomCreatedAsync(long roomId, int userId)
        {
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "room_created",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// A multiplayer room was disbanded.
        /// </summary>
        /// <param name="roomId">The ID of the disbanded room.</param>
        /// <param name="userId">The ID of the user that disbanded the room.</param>
        public async Task OnRoomDisbandedAsync(long roomId, int userId)
        {
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "room_disbanded",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// The <see cref="MultiplayerRoom.State"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newState">The new state of the room.</param>
        public async Task OnRoomStateChangedAsync(long roomId, MultiplayerRoomState newState)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.RoomStateChanged), newState);
        }

        /// <summary>
        /// The <see cref="MultiplayerRoom.Settings"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newSettings">The new settings of the room.</param>
        public async Task OnRoomSettingsChangedAsync(long roomId, MultiplayerRoomSettings newSettings)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), newSettings);
        }

        /// <summary>
        /// The <see cref="MultiplayerRoom.MatchState"/> of the given room changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="newMatchState">The new match state of the room.</param>
        public async Task OnMatchRoomStateChangedAsync(long roomId, MatchRoomState? newMatchState)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), newMatchState);
        }

        /// <summary>
        /// A <see cref="MatchServerEvent"/> has occurred in the room.
        /// </summary>
        /// <remarks>
        /// This should be used for events which have no permanent effect on state.
        /// For operations which are intended to persist (and be visible to new users which join a room)
        /// use <see cref="OnMatchRoomStateChangedAsync"/> or <see cref="OnMatchUserStateChangedAsync"/> instead.
        /// </remarks>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="e">The relevant match event.</param>
        public async Task OnMatchEventAsync(long roomId, MatchServerEvent e)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        /// <summary>
        /// A user has joined the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who joined.</param>
        public async Task OnUserJoinedAsync(long roomId, MultiplayerRoomUser user)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserJoined), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_joined",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has left the given room on their own accord.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who left.</param>
        public async Task OnUserLeftAsync(long roomId, MultiplayerRoomUser user)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserLeft), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_left",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has been forcibly removed from the room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="user">The user who was kicked.</param>
        public async Task OnUserKickedAsync(long roomId, MultiplayerRoomUser user)
        {
            // the target user has already been removed from the group, so send the message to them separately.
            await multiplayerHubContext.Clients.User(user.UserID.ToString()).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "player_kicked",
                room_id = roomId,
                user_id = user.UserID,
            });
        }

        /// <summary>
        /// A user has been invited to join the given room.
        /// </summary>
        /// <param name="roomId">The ID of the room that the invitation pertains to.</param>
        /// <param name="invitedUserId">The ID of the user who was invited to the room.</param>
        /// <param name="invitedBy">The ID of the user who sent the invite.</param>
        /// <param name="password">The password to the given room.</param>
        public async Task OnUserInvitedAsync(long roomId, int invitedUserId, int invitedBy, string password)
        {
            await multiplayerHubContext.Clients.User(invitedUserId.ToString()).SendAsync(nameof(IMultiplayerClient.Invited), invitedBy, roomId, password);
        }

        /// <summary>
        /// The user with the given ID was made host of the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the user who was made host.</param>
        public async Task OnHostChangedAsync(long roomId, int userId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.HostChanged), userId);
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "host_changed",
                room_id = roomId,
                user_id = userId,
            });
        }

        /// <summary>
        /// A user's state in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newUserState">The new state of the user in the room.</param>
        public async Task OnUserStateChangedAsync(long roomId, int userId, MultiplayerUserState newUserState)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserStateChanged), userId, newUserState);
        }

        /// <summary>
        /// A user's <see cref="MultiplayerRoomUser.MatchState"/> in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newMatchUserState">The new match state of the user in the room.</param>
        public async Task OnMatchUserStateChangedAsync(long roomId, int userId, MatchUserState? newMatchUserState)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), userId, newMatchUserState);
        }

        /// <summary>
        /// A user's <see cref="BeatmapAvailability"/> in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newBeatmapAvailability">The new beatmap availability for the user in the room.</param>
        public async Task OnUserBeatmapAvailabilityChangedAsync(long roomId, int userId, BeatmapAvailability newBeatmapAvailability)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserBeatmapAvailabilityChanged), userId, newBeatmapAvailability);
        }

        /// <summary>
        /// A user's selected style in a room has changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="beatmapId">The ID of the difficulty selected by the user.</param>
        /// <param name="rulesetId">The ID of the ruleset selected by the user.</param>
        public async Task OnUserStyleChangedAsync(long roomId, int userId, int? beatmapId, int? rulesetId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserStyleChanged), userId, beatmapId, rulesetId);
        }

        /// <summary>
        /// A user's selected free mods in a room have changed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="newMods">The mods selected by the user.</param>
        public async Task OnUserModsChangedAsync(long roomId, int userId, IEnumerable<APIMod> newMods)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), userId, newMods);
        }

        /// <summary>
        /// A playlist item has been added in the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="item">The playlist item which was added.</param>
        public async Task OnPlaylistItemAddedAsync(long roomId, MultiplayerPlaylistItem item)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        /// <summary>
        /// The given playlist item in the given room has been modified.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="item">The playlist item which was changed.</param>
        public async Task OnPlaylistItemChangedAsync(long roomId, MultiplayerPlaylistItem item)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        /// <summary>
        /// The given playlist item in the given room has been removed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="playlistItemId">The ID of the removed playlist item.</param>
        public async Task OnPlaylistItemRemovedAsync(long roomId, long playlistItemId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        /// <summary>
        /// A match has been started in a given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="playlistItemId">The ID of the playlist item which is being played.</param>
        /// <param name="details">Relevant details about the match configuration to be relayed further.</param>
        public async Task OnMatchStartedAsync(long roomId, long playlistItemId, MatchStartedEventDetail details)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.LoadRequested));
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "game_started",
                room_id = roomId,
                playlist_item_id = playlistItemId,
                event_detail = JsonConvert.SerializeObject(details)
            });
        }

        /// <summary>
        /// Signals the given spectator that a game is in progress.
        /// </summary>
        /// <param name="userId"></param>
        public async Task OnSpectatedMatchInProgressAsync(int userId)
        {
            await multiplayerHubContext.Clients.User(userId.ToString()).SendAsync(nameof(IMultiplayerClient.LoadRequested));
        }

        /// <summary>
        /// Signals the given user that they can begin gameplay.
        /// </summary>
        /// <param name="userId">The ID of the relevant user.</param>
        public async Task OnGameplayStartedAsync(int userId)
        {
            await multiplayerHubContext.Clients.User(userId.ToString()).SendAsync(nameof(IMultiplayerClient.GameplayStarted));
        }

        /// <summary>
        /// The given user has changed their vote whether to skip intro.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="userId">The ID of the relevant user.</param>
        /// <param name="voted">The user's vote to skip intro.</param>
        public async Task OnUserVotedToSkipIntroAsync(long roomId, int userId, bool voted)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.UserVotedToSkipIntro), userId, voted);
        }

        /// <summary>
        /// The vote to skip intro has passed in the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        public async Task OnVoteToSkipIntroPassedAsync(long roomId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.VoteToSkipIntroPassed));
        }

        /// <summary>
        /// A match in the given room has been aborted.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="playlistItemId">The ID of the playlist item which was being played.</param>
        public async Task OnMatchAbortedAsync(long roomId, long playlistItemId)
        {
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "game_aborted",
                room_id = roomId,
                playlist_item_id = playlistItemId,
            });
        }

        /// <summary>
        /// Communicates the reasoning for aborting the match to the given room.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="abortReason">The reason given for aborting the match.</param>
        public async Task OnMatchAbortReasonGivenAsync(long roomId, GameplayAbortReason abortReason)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.GameplayAborted), abortReason);
        }

        /// <summary>
        /// A match in the given room has completed.
        /// </summary>
        /// <param name="roomId">The ID of the relevant room.</param>
        /// <param name="playlistItemId">The ID of the playlist item which was played.</param>
        public async Task OnMatchCompletedAsync(long roomId, long playlistItemId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMultiplayerClient.ResultsReady));
            await logToDatabase(new multiplayer_realtime_room_event
            {
                event_type = "game_completed",
                room_id = roomId,
                playlist_item_id = playlistItemId,
            });
        }

        #region Matchmaking

        public async Task OnMatchmakingRoomCreatedAsync(long roomId, MatchmakingRoomCreatedEventDetail details)
        {
            await logToDatabase(new matchmaking_room_event
            {
                event_type = "room_created",
                room_id = roomId,
                event_detail = JsonConvert.SerializeObject(details)
            });
        }

        public async Task OnPlayerJoinedMatchmakingRoomAsync(long roomId, int userId)
        {
            await logToDatabase(new matchmaking_room_event
            {
                event_type = "user_join",
                room_id = roomId,
                user_id = userId
            });
        }

        public async Task OnPlayerSelectedBeatmapAsync(long roomId, int userId, long playlistItemId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMatchmakingClient.MatchmakingItemSelected), userId, playlistItemId);
        }

        public async Task OnPlayerDeselectedBeatmapAsync(long roomId, int userId, long playlistItemId)
        {
            await multiplayerHubContext.Clients.Group(GetGroupId(roomId)).SendAsync(nameof(IMatchmakingClient.MatchmakingItemDeselected), userId, playlistItemId);
        }

        /// <summary>
        /// Records a user's individual beatmap selection.
        /// </summary>
        public async Task OnPlayerBeatmapPickFinalised(long roomId, int userId, long playlistItemId)
        {
            await logToDatabase(new matchmaking_room_event
            {
                event_type = "user_pick",
                room_id = roomId,
                user_id = userId,
                playlist_item_id = playlistItemId
            });
        }

        /// <summary>
        /// Records the final gameplay beatmap as selected by the server.
        /// </summary>
        public async Task OnFinalBeatmapSelectedAsync(long roomId, long playlistItemId)
        {
            await logToDatabase(new matchmaking_room_event
            {
                event_type = "gameplay_beatmap",
                room_id = roomId,
                playlist_item_id = playlistItemId
            });
        }

        #endregion

        #region Database logging helpers

        private async Task logToDatabase(multiplayer_realtime_room_event ev)
        {
            try
            {
                using var db = databaseFactory.GetInstance();
                await db.LogRoomEventAsync(ev);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to log multiplayer room event to database");
            }
        }

        private async Task logToDatabase(matchmaking_room_event ev)
        {
            try
            {
                using var db = databaseFactory.GetInstance();
                await db.LogRoomEventAsync(ev);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to log multiplayer room event to database");
            }
        }

        #endregion

        /// <summary>
        /// Get the group ID to be used for multiplayer messaging for the given room.
        /// </summary>
        /// <param name="roomId">The databased room ID.</param>
        public static string GetGroupId(long roomId) => $"room:{roomId}";
    }
}
