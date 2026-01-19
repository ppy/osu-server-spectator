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
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
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

        private readonly IHubContext<MultiplayerHub> context;
        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger logger;
        private readonly MultiplayerEventLogger multiplayerEventLogger;

        public MultiplayerHubContext(
            IHubContext<MultiplayerHub> context,
            MultiplayerEventDispatcher eventDispatcher,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory,
            MultiplayerEventLogger multiplayerEventLogger)
        {
            this.context = context;
            this.eventDispatcher = eventDispatcher;
            this.rooms = rooms;
            this.users = users;
            this.databaseFactory = databaseFactory;
            this.multiplayerEventLogger = multiplayerEventLogger;

            logger = loggerFactory.CreateLogger(nameof(MultiplayerHub).Replace("Hub", string.Empty));
        }

        public Task NotifyNewMatchEvent(ServerMultiplayerRoom room, MatchServerEvent e)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchEvent), e);
        }

        public Task NotifyMatchRoomStateChanged(ServerMultiplayerRoom room)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchRoomStateChanged), room.MatchState);
        }

        public Task NotifyMatchUserStateChanged(ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.MatchUserStateChanged), user.UserID, user.MatchState);
        }

        public Task NotifyPlaylistItemAdded(ServerMultiplayerRoom room, MultiplayerPlaylistItem item)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemAdded), item);
        }

        public Task NotifyPlaylistItemRemoved(ServerMultiplayerRoom room, long playlistItemId)
        {
            return context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemRemoved), playlistItemId);
        }

        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item, bool beatmapChanged)
        {
            if (item.ID == room.Settings.PlaylistItemId)
            {
                await EnsureAllUsersValidStyle(room);
                await UnreadyAllUsers(room, beatmapChanged);
            }

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.PlaylistItemChanged), item);
        }

        public async Task NotifySettingsChanged(ServerMultiplayerRoom room, bool playlistItemChanged)
        {
            await EnsureAllUsersValidStyle(room);

            // this should probably only happen for gameplay-related changes, but let's just keep things simple for now.
            await UnreadyAllUsers(room, playlistItemChanged);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.SettingsChanged), room.Settings);
        }

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
        }

        public async Task UnreadyAllUsers(ServerMultiplayerRoom room, bool resetBeatmapAvailability)
        {
            Log(room, null, "Unreadying all users");

            foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.Idle);

            if (resetBeatmapAvailability)
            {
                Log(room, null, "Resetting all users' beatmap availability");

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

            Log(room, user, $"User style changing from (b:{user.BeatmapId}, r:{user.RulesetId}) to (b:{beatmapId}, r:{rulesetId})");

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
                await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), user.UserID, user.Mods);
            }

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserStyleChanged), user.UserID, beatmapId, rulesetId);
        }

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods, ServerMultiplayerRoom room, MultiplayerRoomUser user)
        {
            var newModList = newMods.ToList();

            if (!room.Controller.CurrentItem.ValidateUserMods(user, newModList, out var validMods))
                throw new InvalidStateException($"Incompatible mods were selected: {string.Join(',', newModList.Except(validMods).Select(m => m.Acronym))}");

            if (user.Mods.SequenceEqual(newModList))
                return;

            user.Mods = newModList;

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserModsChanged), user.UserID, newModList);
        }

        public async Task ChangeAndBroadcastUserState(ServerMultiplayerRoom room, MultiplayerRoomUser user, MultiplayerUserState state)
        {
            Log(room, user, $"User state changed from {user.State} to {state}");

            user.State = state;

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserStateChanged), user.UserID, user.State);

            await room.Controller.HandleUserStateChanged(user);
        }

        public async Task ChangeAndBroadcastUserBeatmapAvailability(ServerMultiplayerRoom room, MultiplayerRoomUser user, BeatmapAvailability newBeatmapAvailability)
        {
            if (user.BeatmapAvailability.Equals(newBeatmapAvailability))
                return;

            user.BeatmapAvailability = newBeatmapAvailability;

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserBeatmapAvailabilityChanged), user.UserID, user.BeatmapAvailability);

            await room.Controller.HandleUserStateChanged(user);
        }

        public async Task ChangeRoomState(ServerMultiplayerRoom room, MultiplayerRoomState newState)
        {
            Log(room, null, $"Room state changing from {room.State} to {newState}");

            room.State = newState;
            using (var db = databaseFactory.GetInstance())
                await db.UpdateRoomStatusAsync(room);

            await eventDispatcher.OnRoomStateChangedAsync(room.RoomID, newState);
        }

        public async Task ChangeUserVoteToSkipIntro(ServerMultiplayerRoom room, MultiplayerRoomUser user, bool voted)
        {
            if (user.VotedToSkipIntro == voted)
                return;

            Log(room, user, $"Changing user vote to skip intro => {voted}");

            user.VotedToSkipIntro = voted;
            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.UserVotedToSkipIntro), user.UserID, voted);
        }

        public async Task StartMatch(ServerMultiplayerRoom room)
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
                await ChangeUserVoteToSkipIntro(room, user, false);

            var readyUsers = room.Users.Where(u => u.IsReadyForGameplay()).ToArray();

            foreach (var u in readyUsers)
                await ChangeAndBroadcastUserState(room, u, MultiplayerUserState.WaitingForLoad);

            await ChangeRoomState(room, MultiplayerRoomState.WaitingForLoad);

            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.LoadRequested));

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
                    await context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.GameplayStarted));
                    anyUserPlaying = true;
                }
                else if (user.State == MultiplayerUserState.WaitingForLoad)
                {
                    await ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                    await context.Clients.Client(connectionId).SendAsync(nameof(IMultiplayerClient.GameplayAborted), GameplayAbortReason.LoadTookTooLong);
                    Log(room, user, "Gameplay aborted because this user took too long to load.");
                }
            }

            if (anyUserPlaying)
                await ChangeRoomState(room, MultiplayerRoomState.Playing);
            else
            {
                await ChangeRoomState(room, MultiplayerRoomState.Open);
                await multiplayerEventLogger.LogGameAbortedAsync(room.RoomID, room.CurrentPlaylistItem.ID);
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
                        await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.ResultsReady));

                        if (anyUserFinishedPlay)
                            await multiplayerEventLogger.LogGameCompletedAsync(room.RoomID, room.CurrentPlaylistItem.ID);
                        else
                            await multiplayerEventLogger.LogGameAbortedAsync(room.RoomID, room.CurrentPlaylistItem.ID);

                        await room.Controller.HandleGameplayCompleted();
                    }

                    break;
            }
        }

        public async Task NotifyMatchmakingItemSelected(ServerMultiplayerRoom room, int userId, long playlistItemId)
        {
            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMatchmakingClient.MatchmakingItemSelected), userId, playlistItemId);
        }

        public async Task NotifyMatchmakingItemDeselected(ServerMultiplayerRoom room, int userId, long playlistItemId)
        {
            await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMatchmakingClient.MatchmakingItemDeselected), userId, playlistItemId);
        }

        public async Task CheckVotesToSkipPassed(ServerMultiplayerRoom room)
        {
            int countVotedUsers = room.Users.Count(u => u.State == MultiplayerUserState.Playing && u.VotedToSkipIntro);
            int countGameplayUsers = room.Users.Count(u => u.State == MultiplayerUserState.Playing);

            if (countVotedUsers >= countGameplayUsers / 2 + 1)
                await context.Clients.Group(MultiplayerHub.GetGroupId(room.RoomID)).SendAsync(nameof(IMultiplayerClient.VoteToSkipIntroPassed));
        }

        public void Log(ServerMultiplayerRoom room, MultiplayerRoomUser? user, string message, LogLevel logLevel = LogLevel.Information)
        {
            logger.Log(logLevel, "[user:{userId}] [room:{roomID}] {message}",
                getLoggableUserIdentifier(user),
                room.RoomID,
                message.Trim());
        }

        public void Error(MultiplayerRoomUser? user, string message, Exception exception)
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
