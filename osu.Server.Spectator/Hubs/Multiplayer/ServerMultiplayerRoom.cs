// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking;
using osu.Server.Spectator.Hubs.Multiplayer.Standard;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public class ServerMultiplayerRoom : MultiplayerRoom
    {
        public IMatchController Controller
        {
            get => matchController ?? throw new InvalidOperationException("Room not initialised.");
            private set => matchController = value;
        }

        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly ILogger<ServerMultiplayerRoom> logger;
        private IMatchController? matchController;

        private ServerMultiplayerRoom(
            long roomId,
            IMultiplayerHubContext hub,
            IDatabaseFactory dbFactory,
            MultiplayerEventDispatcher eventDispatcher,
            ILoggerFactory loggerFactory)
            : base(roomId)
        {
            this.hub = hub;
            this.dbFactory = dbFactory;
            this.eventDispatcher = eventDispatcher;
            logger = loggerFactory.CreateLogger<ServerMultiplayerRoom>();
        }

        /// <summary>
        /// Attempt to retrieve and construct a room from the database backend, based on a room ID specification.
        /// This will check the database backing to ensure things are in a consistent state.
        /// This will also mark the room as active, indicating that this server is now in control of the room's lifetime.
        /// </summary>
        /// <param name="roomId">The room identifier.</param>
        /// <param name="hub">The multiplayer hub context.</param>
        /// <param name="dbFactory">The database factory.</param>
        /// <param name="eventDispatcher">Dispatcher responsible to relaying room events to applicable listeners.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <exception cref="InvalidOperationException">If the room does not exist in the database.</exception>
        /// <exception cref="InvalidStateException">If the match has already ended.</exception>
        public static async Task<ServerMultiplayerRoom> InitialiseAsync(
            long roomId,
            IMultiplayerHubContext hub,
            IDatabaseFactory dbFactory,
            MultiplayerEventDispatcher eventDispatcher,
            ILoggerFactory loggerFactory)
        {
            ServerMultiplayerRoom room = new ServerMultiplayerRoom(roomId, hub, dbFactory, eventDispatcher, loggerFactory);

            // TODO: this call should be transactional, and mark the room as managed by this server instance.
            // This will allow for other instances to know not to reinitialise the room if the host arrives there.
            // Alternatively, we can move lobby retrieval away from osu-web and not require this in the first place.
            // Needs further discussion and consideration either way.
            using (var db = dbFactory.GetInstance())
            {
                room.Log($"Retrieving room {roomId} from database");
                var databaseRoom = await db.GetRealtimeRoomAsync(roomId);

                if (databaseRoom == null)
                    throw new InvalidOperationException("Specified match does not exist.");

                if (databaseRoom.ends_at != null && databaseRoom.ends_at < DateTimeOffset.Now)
                    throw new InvalidStateException("Match has already ended.");

                room.ChannelID = databaseRoom.channel_id;
                room.Settings = new MultiplayerRoomSettings
                {
                    Name = databaseRoom.name,
                    Password = databaseRoom.password,
                    MatchType = databaseRoom.type.ToMatchType(),
                    QueueMode = databaseRoom.queue_mode.ToQueueMode(),
                    AutoStartDuration = TimeSpan.FromSeconds(databaseRoom.auto_start_duration),
                    AutoSkip = databaseRoom.auto_skip
                };

                foreach (var item in await db.GetAllPlaylistItemsAsync(roomId))
                    room.Playlist.Add(item.ToMultiplayerPlaylistItem());

                await room.ChangeMatchType(room.Settings.MatchType);

                room.Log("Marking room active");
                await db.MarkRoomActiveAsync(room);
            }

            return room;
        }

        #region Serialisation

        private static readonly MessagePackSerializerOptions message_pack_options = new MessagePackSerializerOptions(new SignalRUnionWorkaroundResolver());

        /// <summary>
        /// Takes a snapshot of the current state of this <see cref="ServerMultiplayerRoom"/> to be sent to a new client.
        /// </summary>
        public MultiplayerRoom TakeSnapshot()
        {
            foreach (var countdown in ActiveCountdowns)
            {
                var countdownInfo = trackedCountdowns[countdown];

                DateTimeOffset countdownEnd = countdownInfo.StartTime + countdownInfo.Duration;
                TimeSpan timeRemaining = countdownEnd - DateTimeOffset.Now;

                countdown.TimeRemaining = timeRemaining.TotalSeconds > 0 ? timeRemaining : TimeSpan.Zero;
            }

            byte[] roomBytes = MessagePackSerializer.Serialize<MultiplayerRoom>(this, message_pack_options);
            return MessagePackSerializer.Deserialize<MultiplayerRoom>(roomBytes, message_pack_options);
        }

        #endregion

        #region Room state management

        private async Task changeRoomState(MultiplayerRoomState newState)
        {
            Log($"Room state changing from {State} to {newState}");

            State = newState;
            using (var db = dbFactory.GetInstance())
                await db.UpdateRoomStatusAsync(this);

            await eventDispatcher.PostRoomStateChangedAsync(RoomID, newState);
        }

        public async Task UpdateRoomStateIfRequired()
        {
            //check whether a room state change is required.
            switch (State)
            {
                case MultiplayerRoomState.Open:
                    if (Settings.AutoStartEnabled)
                    {
                        bool shouldHaveCountdown = !Controller.CurrentItem.Expired && Users.Any(u => u.State == MultiplayerUserState.Ready);

                        if (shouldHaveCountdown && !ActiveCountdowns.Any(c => c is MatchStartCountdown))
                            await StartCountdown(new MatchStartCountdown { TimeRemaining = Settings.AutoStartDuration }, StartMatch);
                    }

                    break;

                case MultiplayerRoomState.WaitingForLoad:
                    int countGameplayUsers = Users.Count(u => u.State.IsGameplayState());
                    int countReadyUsers = Users.Count(u => u.State == MultiplayerUserState.ReadyForGameplay);

                    // Attempt to start gameplay when no more users need to change states. If all users have aborted, this will abort the match.
                    if (countReadyUsers == countGameplayUsers)
                        await startOrStopGameplay(this);

                    break;

                case MultiplayerRoomState.Playing:
                    if (Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        bool anyUserFinishedPlay = false;

                        foreach (var u in Users.Where(u => u.State == MultiplayerUserState.FinishedPlay))
                        {
                            anyUserFinishedPlay = true;
                            await ChangeAndBroadcastUserState(u, MultiplayerUserState.Results);
                        }

                        await changeRoomState(MultiplayerRoomState.Open);

                        if (anyUserFinishedPlay)
                            await eventDispatcher.PostMatchCompletedAsync(RoomID, CurrentPlaylistItem.ID);
                        else
                            await eventDispatcher.PostMatchAbortedAsync(RoomID, CurrentPlaylistItem.ID);

                        await Controller.HandleGameplayCompleted();
                    }

                    break;
            }
        }

        /// <summary>
        /// Changes this room's settings.
        /// Permissions are not checked and validations are not performed. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="newSettings">The new settings.</param>
        /// <exception cref="InvalidStateException">The provided settings were invalid.</exception>
        public async Task ChangeRoomSettings(MultiplayerRoomSettings newSettings)
        {
            Log("Settings updating");

            // Server is authoritative over the playlist item ID.
            // Todo: This needs to change for tournament mode.
            newSettings.PlaylistItemId = Settings.PlaylistItemId;

            if (Settings.Equals(newSettings))
                return;

            var previousSettings = Settings;

            if (newSettings.MatchType == MatchType.Playlists)
                throw new InvalidStateException("Invalid match type selected");

            try
            {
                Settings = newSettings;
                await updateDatabaseSettings();
            }
            catch
            {
                // rollback settings if an error occurred when updating the database.
                Settings = previousSettings;
                throw;
            }

            if (previousSettings.MatchType != newSettings.MatchType)
            {
                await ChangeMatchType(newSettings.MatchType);
                Log($"Switching room ruleset to {Controller}");
            }

            await Controller.HandleSettingsChanged();
            await NotifySettingsChanged(false);

            await UpdateRoomStateIfRequired();
        }

        private async Task updateDatabaseSettings()
        {
            var playlistItem = Playlist.FirstOrDefault(item => item.ID == Settings.PlaylistItemId);

            if (playlistItem == null)
                throw new InvalidStateException("Attempted to select a playlist item not contained by the room.");

            using (var db = dbFactory.GetInstance())
                await db.UpdateRoomSettingsAsync(this);
        }

        [MemberNotNull(nameof(Controller))]
        public Task ChangeMatchType(MatchType type)
        {
            switch (type)
            {
                case MatchType.Matchmaking:
                    return ChangeMatchType(new MatchmakingMatchController(this, hub, dbFactory, eventDispatcher));

                case MatchType.TeamVersus:
                    return ChangeMatchType(new TeamVersusMatchController(this, hub, dbFactory, eventDispatcher));

                default:
                    return ChangeMatchType(new HeadToHeadMatchController(this, hub, dbFactory, eventDispatcher));
            }
        }

        [MemberNotNull(nameof(Controller))]
        public async Task ChangeMatchType(IMatchController controller)
        {
            Controller = controller;

            await Controller.Initialise();

            foreach (var u in Users)
                await Controller.HandleUserJoined(u);
        }

        /// <summary>
        /// Notifies users in this room that the room's settings have changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, unreadies all users, and stops the current countdown.
        /// </remarks>
        /// <param name="playlistItemChanged">Whether the current playlist item changed.</param>
        public async Task NotifySettingsChanged(bool playlistItemChanged)
        {
            await ensureAllUsersValidStyle();

            // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
            await unreadyAllUsers(playlistItemChanged);

            await eventDispatcher.PostRoomSettingsChangedAsync(RoomID, Settings);
        }

        #endregion

        #region User & user state management

        /// <summary>
        /// Adds a new <paramref name="user"/> to this room.
        /// The addition is communicated to the other users in the room.
        /// Permissions for addition to the room are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        public async Task AddUser(MultiplayerRoomUser user)
        {
            // because match controllers may send subsequent information via Users collection hooks,
            // inform clients before adding user to the room.
            await eventDispatcher.PostUserJoinedAsync(RoomID, user);

            Users.Add(user);
            using (var db = dbFactory.GetInstance())
                await db.AddRoomParticipantAsync(this, user);

            await Controller.HandleUserJoined(user);
        }

        /// <summary>
        /// Removes a user with the given <paramref name="userId"/> from this room.
        /// The removal is communicated to the other users in the room.
        /// Permissions for removal from the room are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <returns>The <see cref="MultiplayerRoomUser"/> who was removed.</returns>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task<MultiplayerRoomUser> RemoveUser(int userId)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);

            if (user == null)
                throw new InvalidStateException("User is not in the expected room.");

            Users.Remove(user);
            using (var db = dbFactory.GetInstance())
                await db.RemoveRoomParticipantAsync(this, user);

            await checkVotesToSkipPassed();

            await Controller.HandleUserLeft(user);
            return user;
        }

        /// <summary>
        /// Invites a player into this room.
        /// Permissions for inviting are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="invitedUserId">The ID of the user being invited.</param>
        /// <param name="invitedBy">The ID of the user sending the invite.</param>
        /// <exception cref="InvalidStateException">The room does not support invitations.</exception>
        public async Task InvitePlayer(int invitedUserId, int invitedBy)
        {
            if (Settings.MatchType == MatchType.Matchmaking)
                throw new InvalidStateException("Can't invite players to matchmaking rooms.");

            await eventDispatcher.PostUserInvitedAsync(RoomID, invitedUserId, invitedBy, Settings.Password);
        }

        /// <summary>
        /// Sets the user with the given <paramref name="userId"/> as host of this room.
        /// The host change is communicated to the other users in the room.
        /// Permissions for giving host are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="userId">The ID of the user who host should be given to.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task SetHost(int userId)
        {
            var newHost = Users.FirstOrDefault(u => u.UserID == userId);

            if (newHost == null)
                throw new InvalidStateException("User is not in the expected room.");

            Host = newHost;
            await eventDispatcher.PostHostChangedAsync(RoomID, newHost.UserID);

            using (var db = dbFactory.GetInstance())
                await db.UpdateRoomHostAsync(this);
        }

        /// <summary>
        /// Changes the state of the user with the given <paramref name="userId"/> to <paramref name="newState"/>.
        /// Permissions for changing state are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="userId">The ID of the user whose state should change.</param>
        /// <param name="newState">The new state.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task ChangeUserState(int userId, MultiplayerUserState newState)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);

            if (user == null)
                throw new InvalidStateException("User is not in the expected room.");

            if (user.State == newState)
                return;

            // There's a potential that a client attempts to change state while a message from the server is in transit. Silently block these changes rather than informing the client.
            switch (newState)
            {
                // If a client triggered `Idle` (ie. un-readying) before they received the `WaitingForLoad` message from the match starting.
                case MultiplayerUserState.Idle:
                    if (user.State.IsGameplayState())
                        return;

                    break;

                // If a client a triggered gameplay state before they received the `Idle` message from their gameplay being aborted.
                case MultiplayerUserState.Loaded:
                case MultiplayerUserState.ReadyForGameplay:
                    if (!user.State.IsGameplayState())
                        return;

                    break;
            }

            Log(user, $"User changing state from {user.State} to {newState}");

            ensureValidStateSwitch(user.State, newState);

            await ChangeAndBroadcastUserState(user, newState);

            // Signal newly-spectating users to load gameplay if currently in the middle of play.
            if (newState == MultiplayerUserState.Spectating
                && (State == MultiplayerRoomState.WaitingForLoad || State == MultiplayerRoomState.Playing))
            {
                await eventDispatcher.PostSpectatedMatchInProgressAsync(user.UserID);
            }

            await UpdateRoomStateIfRequired();
        }

        /// <summary>
        /// Given this room and a state transition, throw if there's an issue with the sequence of events.
        /// </summary>
        /// <param name="oldState">The old state.</param>
        /// <param name="newState">The new state.</param>
        private void ensureValidStateSwitch(MultiplayerUserState oldState, MultiplayerUserState newState)
        {
            switch (newState)
            {
                case MultiplayerUserState.Idle:
                    if (oldState.IsGameplayState())
                        throw new InvalidStateException("Cannot return to idle without aborting gameplay.");

                    // any non-gameplay state can return to idle.
                    break;

                case MultiplayerUserState.Ready:
                    if (oldState != MultiplayerUserState.Idle)
                        throw new InvalidStateChangeException(oldState, newState);

                    if (Controller.CurrentItem.Expired)
                        throw new InvalidStateException("Cannot ready up while all items have been played.");

                    break;

                case MultiplayerUserState.WaitingForLoad:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.Loaded:
                    if (oldState != MultiplayerUserState.WaitingForLoad)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.ReadyForGameplay:
                    if (oldState != MultiplayerUserState.Loaded)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.Playing:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.FinishedPlay:
                    if (oldState != MultiplayerUserState.Playing)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                case MultiplayerUserState.Results:
                    // state is managed by the server.
                    throw new InvalidStateChangeException(oldState, newState);

                case MultiplayerUserState.Spectating:
                    if (oldState != MultiplayerUserState.Idle && oldState != MultiplayerUserState.Ready)
                        throw new InvalidStateChangeException(oldState, newState);

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }

        public async Task ChangeAndBroadcastUserState(MultiplayerRoomUser user, MultiplayerUserState state)
        {
            Log(user, $"User state changed from {user.State} to {state}");

            user.State = state;
            await eventDispatcher.PostUserStateChangedAsync(RoomID, user.UserID, user.State);

            await Controller.HandleUserStateChanged(user);
        }

        /// <summary>
        /// Changes the beatmap availability of the user with the given <paramref name="userId"/> to <paramref name="newBeatmapAvailability"/>.
        /// Permissions for changing state are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="userId">The ID of the user whose availability should change.</param>
        /// <param name="newBeatmapAvailability">The new availability.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task ChangeUserBeatmapAvailability(int userId, BeatmapAvailability newBeatmapAvailability)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);

            if (user == null)
                throw new InvalidStateException("User was not in the expected room.");

            await changeAndBroadcastUserBeatmapAvailability(user, newBeatmapAvailability);
        }

        private async Task changeAndBroadcastUserBeatmapAvailability(MultiplayerRoomUser user, BeatmapAvailability newBeatmapAvailability)
        {
            if (user.BeatmapAvailability.Equals(newBeatmapAvailability))
                return;

            user.BeatmapAvailability = newBeatmapAvailability;
            await eventDispatcher.PostUserBeatmapAvailabilityChangedAsync(RoomID, user.UserID, user.BeatmapAvailability);

            await Controller.HandleUserStateChanged(user);
        }

        private async Task unreadyAllUsers(bool resetBeatmapAvailability)
        {
            Log("Unreadying all users");

            foreach (var u in Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                await ChangeAndBroadcastUserState(u, MultiplayerUserState.Idle);

            if (resetBeatmapAvailability)
            {
                Log("Resetting all users' beatmap availability");

                foreach (var user in Users)
                    await changeAndBroadcastUserBeatmapAvailability(user, new BeatmapAvailability(DownloadState.Unknown));
            }

            // Assume some destructive operation took place to warrant unreadying all users, and pre-emptively stop any match start countdown.
            // For example, gameplay-specific changes to the match settings or the current playlist item.
            await stopAllCountdowns<MatchStartCountdown>();
        }

        /// <summary>
        /// Changes the selected style of the user with the given <paramref name="userId"/>.
        /// Permissions for changing state are not checked. Callers are expected to perform relevant checks themselves.
        /// </summary>
        /// <param name="userId">The ID of the target user.</param>
        /// <param name="beatmapId">The beatmap ID of the difficulty picked by the user.</param>
        /// <param name="rulesetId">The ID of the ruleset picked by the user.</param>
        /// <exception cref="InvalidStateException">
        /// The user with the supplied <paramref name="userId"/> was not in the room,
        /// or the new selection is not valid for the current playlist item.
        /// </exception>
        public async Task ChangeUserStyle(int userId, int? beatmapId, int? rulesetId)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);

            if (user == null)
                throw new InvalidStateException("User is not in the expected room.");

            await changeUserStyle(user, beatmapId, rulesetId);
        }

        private async Task changeUserStyle(MultiplayerRoomUser user, int? beatmapId, int? rulesetId)
        {
            if (user.BeatmapId == beatmapId && user.RulesetId == rulesetId)
                return;

            Log(user, $"User style changing from (b:{user.BeatmapId}, r:{user.RulesetId}) to (b:{beatmapId}, r:{rulesetId})");

            if (rulesetId < 0 || rulesetId > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                throw new InvalidStateException("Attempted to select an unsupported ruleset.");

            if (beatmapId != null || rulesetId != null)
            {
                if (!Controller.CurrentItem.Freestyle)
                    throw new InvalidStateException("Current item does not allow free user styles.");

                using (var db = dbFactory.GetInstance())
                {
                    database_beatmap itemBeatmap = (await db.GetBeatmapAsync(Controller.CurrentItem.BeatmapID))!;
                    database_beatmap? userBeatmap = beatmapId == null ? itemBeatmap : await db.GetBeatmapAsync(beatmapId.Value);

                    if (userBeatmap == null)
                        throw new InvalidStateException("Invalid beatmap selected.");

                    if (userBeatmap.beatmapset_id != itemBeatmap.beatmapset_id)
                        throw new InvalidStateException("Selected beatmap is not from the same beatmap set.");

                    if (rulesetId != null && userBeatmap.playmode != 0 && rulesetId != userBeatmap.playmode)
                        throw new InvalidStateException("Selected ruleset is not supported for the given beatmap.");
                }
            }

            user.BeatmapId = beatmapId;
            user.RulesetId = rulesetId;

            if (!Controller.CurrentItem.ValidateUserMods(user, user.Mods, out var validMods))
            {
                user.Mods = validMods.ToArray();
                await eventDispatcher.PostUserModsChangedAsync(RoomID, user.UserID, user.Mods);
            }

            await eventDispatcher.PostUserStyleChangedAsync(RoomID, user.UserID, beatmapId, rulesetId);
        }

        /// <summary>
        /// Changes the selected mods of the user with the given <paramref name="userId"/>.
        /// </summary>
        /// <param name="userId">The ID of the target user.</param>
        /// <param name="newMods">The new mod selection.</param>
        /// <exception cref="InvalidStateException">
        /// The user with the supplied <paramref name="userId"/> was not in the room,
        /// or the new selection is not valid for the current playlist item.
        /// </exception>
        public async Task ChangeUserMods(int userId, IEnumerable<APIMod> newMods)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);

            if (user == null)
                throw new InvalidStateException("User is not in the expected room.");

            await changeUserMods(user, newMods);
        }

        private async Task changeUserMods(MultiplayerRoomUser user, IEnumerable<APIMod> newMods)
        {
            var newModList = newMods.ToList();

            if (!Controller.CurrentItem.ValidateUserMods(user, newModList, out var validMods))
                throw new InvalidStateException($"Incompatible mods were selected: {string.Join(',', newModList.Except(validMods).Select(m => m.Acronym))}");

            if (user.Mods.SequenceEqual(newModList))
                return;

            user.Mods = newModList;

            await eventDispatcher.PostUserModsChangedAsync(RoomID, user.UserID, newModList);
        }

        private async Task ensureAllUsersValidStyle()
        {
            if (!Controller.CurrentItem.Freestyle)
            {
                // Reset entire style when freestyle is disabled.
                foreach (var user in Users)
                    await changeUserStyle(user, null, null);
            }
            else
            {
                database_beatmap itemBeatmap;
                database_beatmap[] validDifficulties;

                using (var db = dbFactory.GetInstance())
                {
                    itemBeatmap = (await db.GetBeatmapAsync(Controller.CurrentItem.BeatmapID))!;
                    validDifficulties = await db.GetBeatmapsAsync(itemBeatmap.beatmapset_id);
                }

                foreach (var user in Users)
                {
                    int? userBeatmapId = user.BeatmapId;
                    int? userRulesetId = user.RulesetId;

                    database_beatmap? foundBeatmap = validDifficulties.SingleOrDefault(b => b.beatmap_id == userBeatmapId);

                    // Reset beatmap style if it's not a valid difficulty for the current beatmap set.
                    if (userBeatmapId != null && foundBeatmap == null)
                        userBeatmapId = null;

                    int beatmapRuleset = foundBeatmap?.playmode ?? itemBeatmap.playmode;

                    // Reset ruleset style when it's no longer valid for the resolved beatmap.
                    if (userRulesetId != null && beatmapRuleset > 0 && userRulesetId != beatmapRuleset)
                        userRulesetId = null;

                    await changeUserStyle(user, userBeatmapId, userRulesetId);
                }
            }

            foreach (var user in Users)
            {
                if (!Controller.CurrentItem.ValidateUserMods(user, user.Mods, out var validMods))
                    await changeUserMods(user, validMods);
            }
        }

        #endregion

        #region Playlist management

        /// <summary>
        /// Adds a new playlist item from the user with the given <see cref="userId"/>.
        /// Permissions are not checked. Callers should check themselves, or via <see cref="Controller"/>.
        /// </summary>
        /// <param name="userId">The ID of the user adding the item.</param>
        /// <param name="item">The playlist item.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task AddPlaylistItem(int userId, MultiplayerPlaylistItem item)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);
            if (user == null)
                throw new InvalidStateException("User is not in expected room.");

            Log(user, $"Adding playlist item for beatmap {item.BeatmapID}");
            await Controller.AddPlaylistItem(item, user);

            await UpdateRoomStateIfRequired();
        }

        /// <summary>
        /// Edits an existing playlist item from the user with the given <see cref="userId"/>.
        /// Permissions are not checked. Callers should check themselves, or via <see cref="Controller"/>.
        /// </summary>
        /// <param name="userId">The ID of the user editing the item.</param>
        /// <param name="item">The playlist item.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task EditPlaylistItem(int userId, MultiplayerPlaylistItem item)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);
            if (user == null)
                throw new InvalidStateException("User is not in expected room.");

            Log(user, $"Editing playlist item {item.ID} for beatmap {item.BeatmapID}");
            await Controller.EditPlaylistItem(item, user);
        }

        /// <summary>
        /// Removes an existing playlist item as the user with the given <see cref="userId"/>.
        /// Permissions are not checked. Callers should check themselves, or via <see cref="Controller"/>.
        /// </summary>
        /// <param name="userId">The ID of the user editing the item.</param>
        /// <param name="playlistItemId">The ID of the playlist item.</param>
        /// <exception cref="InvalidStateException">The user with the supplied <paramref name="userId"/> was not in the room.</exception>
        public async Task RemovePlaylistItem(int userId, long playlistItemId)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);
            if (user == null)
                throw new InvalidStateException("User is not in expected room.");

            Log(user, $"Removing playlist item {playlistItemId}");
            await Controller.RemovePlaylistItem(playlistItemId, user);

            await UpdateRoomStateIfRequired();
        }

        /// <summary>
        /// Notifies users in the room that a playlist item has been changed.
        /// </summary>
        /// <remarks>
        /// Adjusts user mod selections to ensure mod validity, and unreadies all users and stops the current countdown if the currently-selected playlist item was changed.
        /// </remarks>
        /// <param name="item">The changed item.</param>
        /// <param name="beatmapChanged">Whether the beatmap changed.</param>
        public async Task NotifyPlaylistItemChanged(MultiplayerPlaylistItem item, bool beatmapChanged)
        {
            if (item.ID == Settings.PlaylistItemId)
            {
                await ensureAllUsersValidStyle();
                await unreadyAllUsers(beatmapChanged);
            }

            await eventDispatcher.PostPlaylistItemChangedAsync(RoomID, item);
        }

        #endregion

        #region Playing matches

        /// <summary>
        /// The amount of time allowed for players to finish loading gameplay before they're either forced into gameplay (if loaded) or booted to the menu (if still loading).
        /// </summary>
        private static readonly TimeSpan gameplay_load_timeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Starts a match in a room.
        /// </summary>
        /// <param name="room">The room to start the match for.</param>
        /// <exception cref="InvalidStateException">If the current playlist item is expired or the room is not in an <see cref="MultiplayerRoomState.Open"/> state.</exception>
        public static async Task StartMatch(ServerMultiplayerRoom room)
        {
            if (room.State != MultiplayerRoomState.Open)
                throw new InvalidStateException("Can't start match when already in a running state.");

            if (room.Controller.CurrentItem.Expired)
                throw new InvalidStateException("Cannot start an expired playlist item.");

            // If no users are ready, skip the current item in the queue.
            if (room.Users.All(u => u.State != MultiplayerUserState.Ready))
            {
                await room.Controller.HandleGameplayCompleted();
                return;
            }

            // This is the very first time users get a "gameplay" state. Reset any properties for the gameplay session.
            foreach (var user in room.Users)
                await room.changeUserVoteToSkipIntro(user, false);

            var readyUsers = room.Users.Where(u => u.IsReadyForGameplay()).ToArray();

            foreach (var u in readyUsers)
                await room.ChangeAndBroadcastUserState(u, MultiplayerUserState.WaitingForLoad);

            await room.changeRoomState(MultiplayerRoomState.WaitingForLoad);

            await room.eventDispatcher.PostMatchStartedAsync(room.RoomID, room.Controller.CurrentItem.ID, room.Controller.GetMatchDetails());

            await room.StartCountdown(new ForceGameplayStartCountdown { TimeRemaining = gameplay_load_timeout }, startOrStopGameplay);
        }

        /// <summary>
        /// Starts gameplay for all users in the <see cref="MultiplayerUserState.Loaded"/> or <see cref="MultiplayerUserState.ReadyForGameplay"/> states,
        /// and aborts gameplay for any others in the <see cref="MultiplayerUserState.WaitingForLoad"/> state.
        /// </summary>
        private static async Task startOrStopGameplay(ServerMultiplayerRoom room)
        {
            Debug.Assert(room.State == MultiplayerRoomState.WaitingForLoad);

            await room.stopAllCountdowns<ForceGameplayStartCountdown>();

            bool anyUserPlaying = false;

            // Start gameplay for users that are able to, and abort the others which cannot.
            foreach (var user in room.Users)
            {
                if (user.CanStartGameplay())
                {
                    await room.ChangeAndBroadcastUserState(user, MultiplayerUserState.Playing);
                    await room.eventDispatcher.PostGameplayStartedAsync(user.UserID);
                    anyUserPlaying = true;
                }
                else if (user.State == MultiplayerUserState.WaitingForLoad)
                {
                    await room.ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
                    await room.eventDispatcher.PostGameplayAbortedAsync(user.UserID, GameplayAbortReason.LoadTookTooLong);
                    room.Log(user, "Gameplay aborted because this user took too long to load.");
                }
            }

            if (anyUserPlaying)
                await room.changeRoomState(MultiplayerRoomState.Playing);
            else
            {
                await room.changeRoomState(MultiplayerRoomState.Open);
                await room.eventDispatcher.PostMatchAbortedAsync(room.RoomID, room.CurrentPlaylistItem.ID);
                await room.Controller.HandleGameplayCompleted();
            }
        }

        /// <summary>
        /// Submits a user's vote to skip the intro.
        /// </summary>
        /// <param name="userId">The relevant user's ID.</param>
        /// <exception cref="InvalidStateException">
        /// The user was not in this room, or was not in the correct state to vote to skip.
        /// </exception>
        public async Task VoteToSkipIntro(int userId)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);
            if (user == null)
                throw new InvalidStateException("User is not in expected room");

            if (!user.State.IsGameplayState())
                throw new InvalidStateException("Cannot skip while not in a gameplay state");

            await changeUserVoteToSkipIntro(user, true);
            await checkVotesToSkipPassed();
        }

        private async Task changeUserVoteToSkipIntro(MultiplayerRoomUser user, bool voted)
        {
            if (user.VotedToSkipIntro == voted)
                return;

            Log(user, $"Changing user vote to skip intro => {voted}");

            user.VotedToSkipIntro = voted;
            await eventDispatcher.PostUserVotedToSkipIntroAsync(RoomID, user.UserID, voted);
        }

        private async Task checkVotesToSkipPassed()
        {
            int countVotedUsers = Users.Count(u => u.State == MultiplayerUserState.Playing && u.VotedToSkipIntro);
            int countGameplayUsers = Users.Count(u => u.State == MultiplayerUserState.Playing);

            if (countVotedUsers >= countGameplayUsers / 2 + 1)
                await eventDispatcher.PostVoteToSkipIntroPassedAsync(RoomID);
        }

        /// <summary>
        /// Aborts the gameplay of a given user.
        /// </summary>
        /// <param name="userId">The relevant user's ID.</param>
        /// <exception cref="InvalidStateException">
        /// The user was not in this room, or was not in the correct state to abort.
        /// </exception>
        public async Task AbortGameplay(int userId)
        {
            var user = Users.FirstOrDefault(u => u.UserID == userId);
            if (user == null)
                throw new InvalidStateException("User is not in expected room.");

            if (!user.State.IsGameplayState())
                throw new InvalidStateException("Cannot abort gameplay while not in a gameplay state.");

            await ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
            await UpdateRoomStateIfRequired();
        }

        /// <summary>
        /// Aborts an ongoing match.
        /// Permissions are not checked. Callers should implement tne necessary checks themselves.
        /// </summary>
        /// <exception cref="InvalidStateException">If a match is not in progress.</exception>
        public async Task AbortMatch()
        {
            if (State != MultiplayerRoomState.WaitingForLoad && State != MultiplayerRoomState.Playing)
                throw new InvalidStateException("Cannot abort a match that hasn't started.");

            foreach (var user in Users)
                await ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);

            await eventDispatcher.PostMatchAbortReasonGivenAsync(RoomID, GameplayAbortReason.HostAbortedTheMatch);

            await UpdateRoomStateIfRequired();
        }

        #endregion

        #region Countdowns

        private int nextCountdownId;
        private readonly Dictionary<MultiplayerCountdown, CountdownInfo> trackedCountdowns = new Dictionary<MultiplayerCountdown, CountdownInfo>();

        /// <summary>
        /// Starts a new countdown.
        /// </summary>
        /// <param name="countdown">The countdown to start. The <see cref="MultiplayerRoom"/> will receive this object for the duration of the countdown.</param>
        /// <param name="onComplete">A callback to be invoked when the countdown completes.</param>
        public async Task StartCountdown<T>(T countdown, Func<ServerMultiplayerRoom, Task>? onComplete = null)
            where T : MultiplayerCountdown
        {
            if (countdown.IsExclusive)
                await stopAllCountdowns<T>();

            countdown.ID = Interlocked.Increment(ref nextCountdownId);

            CountdownInfo countdownInfo = new CountdownInfo(countdown);

            trackedCountdowns[countdown] = countdownInfo;
            ActiveCountdowns.Add(countdown);

            await eventDispatcher.PostMatchEventAsync(RoomID, new CountdownStartedEvent(countdown));

            countdownInfo.Task = start();

            async Task start()
            {
                // Run the countdown.
                try
                {
                    using (var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(countdownInfo.StopSource.Token, countdownInfo.SkipSource.Token))
                        await Task.Delay(countdownInfo.Duration, cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Clients need to be notified of cancellations in the following code.
                }

                // Notify users that the countdown has finished (or cancelled) and run the continuation.
                // Note: The room must be re-retrieved rather than using our own instance to enforce single-thread access.
                using (var roomUsage = await hub.TryGetRoom(RoomID))
                {
                    try
                    {
                        if (roomUsage?.Item == null)
                            return;

                        if (countdownInfo.StopSource.IsCancellationRequested)
                            return;

                        Debug.Assert(trackedCountdowns.ContainsKey(countdown));
                        Debug.Assert(ActiveCountdowns.Contains(countdown));

                        await StopCountdown(countdown);

                        // The continuation could be run outside of the room lock, however it seems saner to run it within the same lock as the cancellation token usage.
                        // Furthermore, providing a room-id instead of the room becomes cumbersome for usages, so this also provides a nicer API.
                        if (onComplete != null)
                            await onComplete(roomUsage.Item);
                    }
                    finally
                    {
                        countdownInfo.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Stops all countdowns of the given type, preventing their callbacks from running.
        /// </summary>
        /// <typeparam name="T">The countdown type.</typeparam>
        private async Task stopAllCountdowns<T>()
            where T : MultiplayerCountdown
        {
            foreach (var countdown in ActiveCountdowns.OfType<T>().ToArray())
                await StopCountdown(countdown);
        }

        /// <summary>
        /// Stops the given countdown, preventing its callback from running.
        /// </summary>
        /// <param name="countdown">The countdown to stop.</param>
        public async Task StopCountdown(MultiplayerCountdown countdown)
        {
            if (!trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return;

            countdownInfo.StopSource.Cancel();

            trackedCountdowns.Remove(countdown);
            ActiveCountdowns.Remove(countdownInfo.Countdown);

            await eventDispatcher.PostMatchEventAsync(RoomID, new CountdownStoppedEvent(countdownInfo.Countdown.ID));
        }

        /// <summary>
        /// Skips to the end of the given countdown and runs its callback (e.g. to start the match) as soon as possible unless the countdown has been cancelled.
        /// </summary>
        /// <param name="countdown">The countdown.</param>
        /// <returns>
        /// A task which will become completed when the active countdown completes. Make sure to await this *outside* a usage.
        /// </returns>
        public Task SkipToEndOfCountdown(MultiplayerCountdown? countdown)
        {
            if (countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return Task.CompletedTask;

            countdownInfo.SkipSource.Cancel();
            return countdownInfo.Task;
        }

        /// <summary>
        /// Retrieves the task for the given countdown, if one is running.
        /// </summary>
        /// <param name="countdown">The countdown to retrieve the task of.</param>
        public Task GetCountdownTask(MultiplayerCountdown? countdown)
            => countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo) ? Task.CompletedTask : countdownInfo.Task;

        /// <summary>
        /// Searches the currently active countdowns and retrieves one of the given type.
        /// </summary>
        /// <typeparam name="T">The countdown type.</typeparam>
        /// <returns>A countdown of the given type, or null if no such countdown is running.</returns>
        public T? FindCountdownOfType<T>() where T : MultiplayerCountdown
            => ActiveCountdowns.OfType<T>().FirstOrDefault();

        /// <summary>
        /// Searches the currently active countdowns and retrieves the one matching a given ID.
        /// </summary>
        /// <param name="countdownId">The countdown ID.</param>
        /// <returns>The countdown matching the given ID, or null if no such countdown is running.</returns>
        public MultiplayerCountdown? FindCountdownById(int countdownId)
            => ActiveCountdowns.SingleOrDefault(c => c.ID == countdownId);

        /// <summary>
        /// Retrieves the remaining time for a countdown.
        /// </summary>
        /// <param name="countdown">The countdown.</param>
        /// <returns>The remaining time.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public TimeSpan GetCountdownRemainingTime(MultiplayerCountdown? countdown)
        {
            if (countdown == null || !trackedCountdowns.TryGetValue(countdown, out CountdownInfo? countdownInfo))
                return TimeSpan.Zero;

            TimeSpan elapsed = DateTimeOffset.Now - countdownInfo.StartTime;
            return elapsed >= countdownInfo.Duration ? TimeSpan.Zero : countdownInfo.Duration - elapsed;
        }

        private class CountdownInfo : IDisposable
        {
            public readonly MultiplayerCountdown Countdown;
            public readonly CancellationTokenSource StopSource = new CancellationTokenSource();
            public readonly CancellationTokenSource SkipSource = new CancellationTokenSource();
            public readonly DateTimeOffset StartTime = DateTimeOffset.Now;
            public readonly TimeSpan Duration;

            public Task Task { get; set; } = null!;

            public CountdownInfo(MultiplayerCountdown countdown)
            {
                Countdown = countdown;
                Duration = countdown.TimeRemaining;
            }

            public void Dispose()
            {
                StopSource.Dispose();
                SkipSource.Dispose();
            }
        }

        #endregion

        #region Logging

        public void Log(string message, LogLevel logLevel = LogLevel.Information)
        {
            logger.Log(logLevel, "[room:{roomID}] {message}",
                RoomID,
                message.Trim());
        }

        public void Log(MultiplayerRoomUser user, string message, LogLevel logLevel = LogLevel.Information)
        {
            logger.Log(logLevel, "[user:{userId}] [room:{roomID}] {message}",
                user.UserID.ToString(),
                RoomID,
                message.Trim());
        }

        #endregion
    }
}
