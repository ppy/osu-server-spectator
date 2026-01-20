// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
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

        private readonly MultiplayerEventDispatcher eventDispatcher;
        private readonly EntityStore<ServerMultiplayerRoom> rooms;
        private readonly EntityStore<MultiplayerClientState> users;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILogger logger;

        public MultiplayerHubContext(
            MultiplayerEventDispatcher eventDispatcher,
            EntityStore<ServerMultiplayerRoom> rooms,
            EntityStore<MultiplayerClientState> users,
            ILoggerFactory loggerFactory,
            IDatabaseFactory databaseFactory)
        {
            this.eventDispatcher = eventDispatcher;
            this.rooms = rooms;
            this.users = users;
            this.databaseFactory = databaseFactory;

            logger = loggerFactory.CreateLogger(nameof(MultiplayerHub).Replace("Hub", string.Empty));
        }

        public async Task NotifyPlaylistItemChanged(ServerMultiplayerRoom room, MultiplayerPlaylistItem item, bool beatmapChanged)
        {
            if (item.ID == room.Settings.PlaylistItemId)
            {
                await room.EnsureAllUsersValidStyle();
                await room.UnreadyAllUsers(beatmapChanged);
            }

            await eventDispatcher.PostPlaylistItemChangedAsync(room.RoomID, item);
        }

        public Task<ItemUsage<ServerMultiplayerRoom>?> TryGetRoom(long roomId)
        {
            return rooms.TryGetForUse(roomId);
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
                await room.ChangeUserVoteToSkipIntro(user, false);

            var readyUsers = room.Users.Where(u => u.IsReadyForGameplay()).ToArray();

            foreach (var u in readyUsers)
                await room.ChangeAndBroadcastUserState(u, MultiplayerUserState.WaitingForLoad);

            await room.ChangeRoomState(MultiplayerRoomState.WaitingForLoad);

            await eventDispatcher.PostMatchStartedAsync(room.RoomID, room.Controller.CurrentItem.ID, room.Controller.GetMatchDetails());

            await room.StartCountdown(new ForceGameplayStartCountdown { TimeRemaining = gameplay_load_timeout }, ServerMultiplayerRoom.StartOrStopGameplay);
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
                    int countGameplayUsers = room.Users.Count(u => u.State.IsGameplayState());
                    int countReadyUsers = room.Users.Count(u => u.State == MultiplayerUserState.ReadyForGameplay);

                    // Attempt to start gameplay when no more users need to change states. If all users have aborted, this will abort the match.
                    if (countReadyUsers == countGameplayUsers)
                        await ServerMultiplayerRoom.StartOrStopGameplay(room);

                    break;

                case MultiplayerRoomState.Playing:
                    if (room.Users.All(u => u.State != MultiplayerUserState.Playing))
                    {
                        bool anyUserFinishedPlay = false;

                        foreach (var u in room.Users.Where(u => u.State == MultiplayerUserState.FinishedPlay))
                        {
                            anyUserFinishedPlay = true;
                            await room.ChangeAndBroadcastUserState(u, MultiplayerUserState.Results);
                        }

                        await room.ChangeRoomState(MultiplayerRoomState.Open);

                        if (anyUserFinishedPlay)
                            await eventDispatcher.PostMatchCompletedAsync(room.RoomID, room.CurrentPlaylistItem.ID);
                        else
                            await eventDispatcher.PostMatchAbortedAsync(room.RoomID, room.CurrentPlaylistItem.ID);

                        await room.Controller.HandleGameplayCompleted();
                    }

                    break;
            }
        }

        public async Task CheckVotesToSkipPassed(ServerMultiplayerRoom room)
        {
            int countVotedUsers = room.Users.Count(u => u.State == MultiplayerUserState.Playing && u.VotedToSkipIntro);
            int countGameplayUsers = room.Users.Count(u => u.State == MultiplayerUserState.Playing);

            if (countVotedUsers >= countGameplayUsers / 2 + 1)
                await eventDispatcher.PostVoteToSkipIntroPassedAsync(room.RoomID);
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
