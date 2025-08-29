// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Services;
using IDatabaseFactory = osu.Server.Spectator.Database.IDatabaseFactory;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Allows communication with multiplayer clients from potentially outside of a direct <see cref="MultiplayerHub"/> context.
    /// </summary>
    public class MultiplayerHubContext : IMultiplayerHubContext
    {
        /// <summary>
        /// The amount of time allowed for players to finish loading gameplay before they're either forced into gameplay (if loaded) or booted to the menu (if still loading).
        /// </summary>
        private static readonly TimeSpan gameplay_load_timeout = TimeSpan.FromSeconds(30);

        public IHubContext<MultiplayerHub> Context { get; }

        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ISharedInterop sharedInterop;
        private readonly ILogger logger;
        private readonly MultiplayerEventLogger multiplayerEventLogger;

        public MultiplayerHubContext(
            IHubContext<MultiplayerHub> context,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            ISharedInterop sharedInterop,
            MultiplayerEventLogger multiplayerEventLogger)
        {
            this.Context = context;
            this.rooms = rooms;
            this.users = users;
            this.databaseFactory = databaseFactory;
            this.sharedInterop = sharedInterop;
            this.multiplayerEventLogger = multiplayerEventLogger;

            logger = loggerFactory.CreateLogger(nameof(MultiplayerHub).Replace("Hub", string.Empty));
        }

        public Task NotifyNewMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e)
        {
            return Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        public Task NotifyMatchRoomStateChanged(ServerMultiplayerRoom room)
        {
            return Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), room.MatchState);
        }

        public Task NotifyMatchUserStateChanged(ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            return Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), user.UserID, user.MatchState);
        }

        public Task NotifyPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            return Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        public Task NotifyPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId)
        {
            return Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item, bool beatmapChanged)
        {
            if (item.ID == room.Settings.PlaylistItemId)
            {
                await EnsureAllUsersValidStyle(room);
                await UnreadyAllUsers(room, beatmapChanged);
            }

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        public async Task NotifySettingsChanged(ServerMultiplayerRoom room, bool playlistItemChanged)
        {
            await EnsureAllUsersValidStyle(room);

            // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
            await UnreadyAllUsers(room, playlistItemChanged);

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), room.Settings);
        }

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
        }

        public async Task UnreadyAllUsers(ServerMultiplayerRoom room, bool resetBeatmapAvailability)
        {
            log(room, null, "Unreadying all users");

            foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Idle);

            if (resetBeatmapAvailability)
            {
                log(room, null, "Resetting all users' beatmap availability");

                foreach (var user in room.Users)
                    await ChangeAndBroadcastUserBeatmapAvailability(room, user, new BeatmapAvailability(DownloadState.Unknown));
            }

            // Assume some destructive operation took place to warrant unreadying all users, and pre-emptively stop any match start countdown.
            // For example, gameplay-specific changes to the match settings or the current playlist item.
            await room.StopAllCountdowns<MatchStartCountdown>();
        }

        public async Task EnsureAllUsersValidStyle(ServerMultiplayerRoom room)
        {
            if (!room.Controller.CurrentItem.Freestyle)
            {
                // Reset entire style when freestyle is disabled.
                foreach (var user in room.Users)
                    await ChangeUserStyle(null, null, room, user);
            }
            else
            {
                database_beatmap itemBeatmap;
                database_beatmap[] validDifficulties;

                using (var db = databaseFactory.GetInstance())
                {
                    itemBeatmap = (await db.GetBeatmapAsync(room.Controller.CurrentItem.BeatmapID))!;
                    validDifficulties = await db.GetBeatmapsAsync(itemBeatmap.beatmapset_id);
                }

                foreach (var user in room.Users)
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

                    await ChangeUserStyle(userBeatmapId, userRulesetId, room, user);
                }
            }

            foreach (var user in room.Users)
            {
                if (!room.Controller.CurrentItem.ValidateUserMods(user, user.Mods, out var validMods))
                    await ChangeUserMods(validMods, room, user);
            }
        }

        public async Task ChangeUserStyle(int? beatmapId, int? rulesetId, ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            if (user.BeatmapId == beatmapId && user.RulesetId == rulesetId)
                return;

            log(room, user, $"User style changing from (b:{user.BeatmapId}, r:{user.RulesetId}) to (b:{beatmapId}, r:{rulesetId})");

            if (rulesetId < 0 || rulesetId > ILegacyRuleset.MAX_LEGACY_RULESET_ID)
                throw new InvalidStateException("Attempted to select an unsupported ruleset.");

            if (beatmapId != null || rulesetId != null)
            {
                if (!room.Controller.CurrentItem.Freestyle)
                    throw new InvalidStateException("Current item does not allow free user styles.");

                using (var db = databaseFactory.GetInstance())
                {
                    database_beatmap itemBeatmap = (await db.GetBeatmapAsync(room.Controller.CurrentItem.BeatmapID))!;
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

            if (!room.Controller.CurrentItem.ValidateUserMods(user, user.Mods, out var validMods))
            {
                user.Mods = validMods.ToArray();
                await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), user.UserID, user.Mods);
            }

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserStyleChanged), user.UserID, beatmapId, rulesetId);
        }

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods, ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            var newModList = newMods.ToList();

            if (!room.Controller.CurrentItem.ValidateUserMods(user, newModList, out var validMods))
                throw new InvalidStateException($"Incompatible mods were selected: {string.Join(',', newModList.Except(validMods).Select(m => m.Acronym))}");

            if (user.Mods.SequenceEqual(newModList))
                return;

            user.Mods = newModList;

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), user.UserID, newModList);
        }

        public async Task ChangeAndBroadcastUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state)
        {
            log(room, user, $"User state changed from {user.State} to {state}");

            user.State = state;

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserStateChanged), user.UserID, user.State);

            await room.Controller.HandleUserStateChanged(user);
        }

        public async Task ChangeAndBroadcastUserBeatmapAvailability(ServerMultiplayerRoom room, MultiplayerRoomUser user, BeatmapAvailability newBeatmapAvailability)
        {
            if (user.BeatmapAvailability.Equals(newBeatmapAvailability))
                return;

            user.BeatmapAvailability = newBeatmapAvailability;

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserBeatmapAvailabilityChanged), user.UserID, user.BeatmapAvailability);
        }

        public async Task ChangeRoomState(ServerMultiplayerRoom room, MultiplayerRoomState newState)
        {
            log(room, null, $"Room state changing from {room.State} to {newState}");

            room.State = newState;
            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomStatusAsync(room);

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.RoomStateChanged), newState);
        }

        public Task StartMatch(ServerMultiplayerRoom room) => StartMatch(room, true);

        public async Task StartMatch(ServerMultiplayerRoom room, bool requireReadyUsers)
        {
            if (room.State != MultiplayerRoomState.Open)
                throw new InvalidStateException("Can't start match when already in a running state.");

            if (room.Controller.CurrentItem.Expired)
                throw new InvalidStateException("Cannot start an expired playlist item.");

            // If no users are ready, skip the current item in the queue.
            if (requireReadyUsers && room.Users.All(u => u.State != MultiplayerUserState.Ready))
            {
                await room.Controller.HandleGameplayCompleted();
                return;
            }

            var readyUsers = room.Users.Where(u =>
                u.BeatmapAvailability.State == DownloadState.LocallyAvailable
                && (u.State == MultiplayerUserState.Ready || u.State == MultiplayerUserState.Idle)
            ).ToArray();

            foreach (var u in readyUsers)
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.WaitingForLoad);

            await ChangeRoomState(room, MultiplayerRoomState.WaitingForLoad);

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.LoadRequested));

            await room.StartCountdown(new ForceGameplayStartCountdown { TimeRemaining = gameplay_load_timeout }, StartOrStopGameplay);

            await multiplayerEventLogger.LogGameStartedAsync(room.RoomID, room.Controller.CurrentItem.ID, room.Controller.GetMatchDetails());
        }

        /// <summary>
        /// Starts gameplay for all users in the <see cref="MultiplayerUserState.Loaded"/> or <see cref="MultiplayerUserState.ReadyForGameplay"/> states,
        /// and aborts gameplay for any others in the <see cref="MultiplayerUserState.WaitingForLoad"/> state.
        /// </summary>
        public async Task StartOrStopGameplay(ServerMultiplayerRoom room)
        {
            Debug.Assert(room.State == MultiplayerRoomState.WaitingForLoad);

            await room.StopAllCountdowns<ForceGameplayStartCountdown>();

            bool anyUserPlaying = false;

            // Start gameplay for users that are able to, and abort the others which cannot.
            foreach (var user in room.Users)
            {
                string? connectionId = users.GetConnectionIdForUser(user.UserID);

                if (connectionId == null)
                    continue;

                if (user.CanStartGameplay())
                {
                    await ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Playing);
                    await Context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.GameplayStarted));
                    anyUserPlaying = true;
                }
                else if (user.State == MultiplayerUserState.WaitingForLoad)
                {
                    await ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                    await Context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.GameplayAborted), GameplayAbortReason.LoadTookTooLong);
                    log(room, user, "Gameplay aborted because this user took too long to load.");
                }
            }

            if (anyUserPlaying)
                await ChangeRoomState(room, MultiplayerRoomState.Playing);
            else
            {
                await ChangeRoomState(room, MultiplayerRoomState.Open);
                await room.Controller.HandleGameplayCompleted();
            }
        }

        public async Task UpdateRoomStateIfRequired(ServerMultiplayerRoom room)
        {
            //check whether a room state change is required.
            switch (room.State)
            {
                case MultiplayerRoomState.Open:
                    if (room.Settings.AutoStartEnabled)
                    {
                        bool shouldHaveCountdown = !room.Controller.CurrentItem.Expired && room.Users.Any(u => u.State == MultiplayerUserState.Ready);

                        if (shouldHaveCountdown && !room.ActiveCountdowns.Any(c => c is MatchStartCountdown))
                            await room.StartCountdown(new MatchStartCountdown { TimeRemaining = room.Settings.AutoStartDuration }, StartMatch);
                    }

                    break;

                case MultiplayerRoomState.WaitingForLoad:
                    int countGameplayUsers = room.Users.Count(u => MultiplayerHub.IsGameplayState(u.State));
                    int countReadyUsers = room.Users.Count(u => u.State == MultiplayerUserState.ReadyForGameplay);

                    // Attempt to start gameplay when no more users need to change states. If all users have aborted, this will abort the match.
                    if (countReadyUsers == countGameplayUsers)
                        await StartOrStopGameplay(room);

                    break;

                case MultiplayerRoomState.Playing:
                    if (room.Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        bool anyUserFinishedPlay = false;

                        foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.FinishedPlay))
                        {
                            anyUserFinishedPlay = true;
                            await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Results);
                        }

                        await ChangeRoomState(room, MultiplayerRoomState.Open);
                        await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.ResultsReady));

                        if (anyUserFinishedPlay)
                            await multiplayerEventLogger.LogGameCompletedAsync(room.RoomID, room.GetCurrentItem()!.ID);
                        else
                            await multiplayerEventLogger.LogGameAbortedAsync(room.RoomID, room.GetCurrentItem()!.ID);

                        await room.Controller.HandleGameplayCompleted();
                    }

                    break;
            }
        }

        public async Task SetNewHost(MultiplayerRoom room, MultiplayerRoomUser newHost)
        {
            room.Host = newHost;

            await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.HostChanged), newHost.UserID);

            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomHostAsync(room);
        }

        public async Task LeaveRoom(MultiplayerClientState state, ServerMultiplayerRoom room, bool wasKick)
        {
            await Context.Groups.RemoveFromGroupAsync(state.ConnectionId, MultiplayerHub.GetGroupId(room.RoomID));

            var user = room.Users.FirstOrDefault(u => u.UserID == state.UserId);
            if (user == null)
                throw new InvalidStateException("User was not in the expected room.");

            log(room, user, wasKick ? "User kicked" : "User left");

            await room.RemoveUser(user);
            await removeDatabaseUser(room, user);

            try
            {
                // Run in background so we don't hold locks on user/room states.
                _ = sharedInterop.RemoveUserFromRoomAsync(state.UserId, state.CurrentRoomID);
            }
            catch
            {
                // Errors are logged internally by SharedInterop.
            }

            // handle closing the room if the only participant is the user which is leaving.
            if (room.Users.Count == 0)
            {
                await EndDatabaseMatch(room, user.UserID);
                destroyRoom(room.RoomID);
                return;
            }

            await UpdateRoomStateIfRequired(room);

            // if this user was the host, we need to arbitrarily transfer host so the room can continue to exist.
            if (room.Host?.Equals(user) == true)
            {
                // there *has* to still be at least one user in the room (see user check above).
                var newHost = room.Users.First();

                await SetNewHost(room, newHost);
            }

            if (wasKick)
            {
                // the target user has already been removed from the group, so send the message to them separately.
                await Context.Clients.Client(state.ConnectionId).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
                await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserKicked), user);
            }
            else
                await Context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserLeft), user);
        }

        public async Task CloseRoom(ServerMultiplayerRoom room)
        {
            foreach (var user in room.Users)
            {
                using (var userUsage = await users.GetForUse(user.UserID))
                {
                    var clientState = userUsage.Item;

                    if (clientState == null)
                        continue;

                    await LeaveRoom(clientState, room, false);
                }
            }
        }

        public async Task EndDatabaseMatch(MultiplayerRoom room, int userId)
        {
            using (var db = databaseFactory.GetInstance())
                await db.EndMatchAsync(room);

            await multiplayerEventLogger.LogRoomDisbandedAsync(room.RoomID, userId);
        }

        private void destroyRoom(long roomId)
        {
            Task.Run(async () =>
            {
                using (var roomUsage = await rooms.GetForUse(roomId))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        return;

                    log(room, null, "Stopping tracking of room.");
                    roomUsage.Destroy();
                }
            });
        }

        private async Task removeDatabaseUser(MultiplayerRoom room, MultiplayerRoomUser user)
        {
            using (var db = databaseFactory.GetInstance())
                await db.RemoveRoomParticipantAsync(room, user);
        }

        private void log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information)
        {
            logger.Log(logLevel, "[user:{userId}] [room:{roomID}] {message}",
                getLoggableUserIdentifier(user),
                room.RoomID,
                message.Trim());
        }

        private void error(MultiplayerRoomUser? user, string message, Exception exception)
        {
            logger.LogError(exception, "[user:{userId}] {message}",
                getLoggableUserIdentifier(user),
                message.Trim());
        }

        private string getLoggableUserIdentifier(MultiplayerRoomUser? user)
        {
            return user?.UserID.ToString() ?? "???";
        }
    }
}
