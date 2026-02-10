// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    [Authorize(ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME)]
    public partial class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        public const string STATSD_PREFIX = "multiplayer";

        protected readonly IMultiplayerRoomController RoomController;
        private readonly IDatabaseFactory databaseFactory;
        private readonly ChatFilters chatFilters;
        private readonly ISharedInterop sharedInterop;
        private readonly MultiplayerEventDispatcher multiplayerEventDispatcher;
        private readonly IMatchmakingQueueBackgroundService matchmakingQueueService;

        public MultiplayerHub(
            ILoggerFactory loggerFactory,
            EntityStore<MultiplayerClientState> users,
            IDatabaseFactory databaseFactory,
            ChatFilters chatFilters,
            IMultiplayerRoomController roomController,
            ISharedInterop sharedInterop,
            MultiplayerEventDispatcher multiplayerEventDispatcher,
            IMatchmakingQueueBackgroundService matchmakingQueueService)
            : base(loggerFactory, users)
        {
            this.databaseFactory = databaseFactory;
            this.chatFilters = chatFilters;
            this.sharedInterop = sharedInterop;
            this.multiplayerEventDispatcher = multiplayerEventDispatcher;
            this.matchmakingQueueService = matchmakingQueueService;

            RoomController = roomController;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            using (var usage = await GetOrCreateLocalUserState())
                usage.Item = new MultiplayerClientState(Context.ConnectionId, Context.GetUserId());
        }

        public async Task<MultiplayerRoom> CreateRoom(MultiplayerRoom room)
        {
            Log("Attempting to create room");

            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't join a room when restricted.");
            }

            long roomId = await sharedInterop.CreateRoomAsync(Context.GetUserId(), room);
            await multiplayerEventDispatcher.PostRoomCreatedAsync(roomId, Context.GetUserId());

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID != null)
                    throw new InvalidStateException("Can't join a room when already in another room.");

                return await RoomController.CreateRoom(userUsage.Item, roomId, room.Settings.Password);
            }
        }

        public Task<MultiplayerRoom> JoinRoom(long roomId) => JoinRoomWithPassword(roomId, string.Empty);

        public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
        {
            Log($"Attempting to join room {roomId}");

            using (var db = databaseFactory.GetInstance())
            {
                if (await db.IsUserRestrictedAsync(Context.GetUserId()))
                    throw new InvalidStateException("Can't join a room when restricted.");
            }

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID != null)
                    throw new InvalidStateException("Can't join a room when already in another room.");

                return await RoomController.JoinRoom(userUsage.Item, roomId, password);
            }
        }

        public async Task LeaveRoom()
        {
            Log("Requesting to leave room");

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                if (userUsage.Item.CurrentRoomID == null)
                    return;

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                    await RoomController.LeaveRoom(userUsage.Item, roomUsage);
            }
        }

        public async Task InvitePlayer(int userId)
        {
            using (var db = databaseFactory.GetInstance())
            {
                bool isRestricted = await db.IsUserRestrictedAsync(userId);
                if (isRestricted)
                    throw new InvalidStateException("Can't invite a restricted user to a room.");

                var relation = await db.GetUserRelation(Context.GetUserId(), userId);

                // The local user has the player they are trying to invite blocked.
                if (relation?.foe == true)
                    throw new UserBlockedException();

                var inverseRelation = await db.GetUserRelation(userId, Context.GetUserId());

                // The player being invited has the local user blocked.
                if (inverseRelation?.foe == true)
                    throw new UserBlockedException();

                // The player being invited disallows unsolicited PMs and the local user is not their friend.
                if (inverseRelation?.friend != true && !await db.GetUserAllowsPMs(userId))
                    throw new UserBlocksPMsException();
            }

            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var user = userUsage.Item;
                    var room = roomUsage.Item;

                    if (user == null)
                        throw new InvalidStateException("Local user was not found in the expected room");

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.InvitePlayer(invitedUserId: userId, invitedBy: user.UserId);
                }
            }
        }

        public async Task TransferHost(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    room.Log($"Transferring host from {room.Host?.UserID} to {userId}");

                    ensureIsHost(room);

                    await room.SetHost(userId);
                }
            }
        }

        public async Task KickUser(int userId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    room.Log($"Kicking user {userId}");

                    if (userId == userUsage.Item.UserId)
                        throw new InvalidStateException("Can't kick self");

                    ensureIsHost(room);

                    var kickTarget = room.Users.FirstOrDefault(u => u.UserID == userId);

                    if (kickTarget == null)
                        throw new InvalidOperationException("Target user is not in the current room");

                    using (var targetUserUsage = await GetStateFromUser(kickTarget.UserID))
                    {
                        Debug.Assert(targetUserUsage.Item != null);

                        if (targetUserUsage.Item.CurrentRoomID == null)
                            throw new InvalidOperationException();

                        await RoomController.KickUserFromRoom(targetUserUsage.Item, roomUsage, userUsage.Item.UserId);
                    }
                }
            }
        }

        public async Task ChangeState(MultiplayerUserState newState)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserState(Context.GetUserId(), newState);
                }
            }
        }

        public async Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserBeatmapAvailability(Context.GetUserId(), newBeatmapAvailability);
                }
            }
        }

        public async Task ChangeUserStyle(int? beatmapId, int? rulesetId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserStyle(Context.GetUserId(), beatmapId, rulesetId);
                }
            }
        }

        public async Task ChangeUserMods(IEnumerable<APIMod> newMods)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.ChangeUserMods(Context.GetUserId(), newMods);
                }
            }
        }

        public async Task SendMatchRequest(MatchUserRequest request)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    var user = room.Users.FirstOrDefault(u => u.UserID == Context.GetUserId());

                    if (user == null)
                        throw new InvalidOperationException("Local user was not found in the expected room");

                    switch (request)
                    {
                        case StartMatchCountdownRequest startMatchCountdownRequest:
                            ensureIsHost(room);
                            await room.StartMatchCountdown(startMatchCountdownRequest.Duration);
                            break;

                        case StopCountdownRequest stopCountdownRequest:
                            ensureIsHost(room);
                            await room.StopCountdown(stopCountdownRequest.ID);
                            break;

                        default:
                            await room.HandleUserRequest(user, request);
                            break;
                    }
                }
            }
        }

        public async Task StartMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    ensureIsHost(room);

                    if (room.Host != null && room.Host.State != MultiplayerUserState.Spectating && room.Host.State != MultiplayerUserState.Ready)
                        throw new InvalidStateException("Can't start match when the host is not ready.");

                    if (room.Users.All(u => u.State != MultiplayerUserState.Ready))
                        throw new InvalidStateException("Can't start match when no users are ready.");

                    await ServerMultiplayerRoom.StartMatch(room);
                }
            }
        }

        public async Task AbortMatch()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    ensureIsHost(room);

                    await room.AbortMatch();
                }
            }
        }

        public async Task AbortGameplay()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.AbortGameplay(Context.GetUserId());
                }
            }
        }

        public async Task VoteToSkipIntro()
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.VoteToSkipIntro(Context.GetUserId());
                }
            }
        }

        public async Task AddPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.AddPlaylistItem(Context.GetUserId(), item);
                }
            }
        }

        public async Task EditPlaylistItem(MultiplayerPlaylistItem item)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.EditPlaylistItem(Context.GetUserId(), item);
                }
            }
        }

        public async Task RemovePlaylistItem(long playlistItemId)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;
                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    await room.RemovePlaylistItem(Context.GetUserId(), playlistItemId);
                }
            }
        }

        public async Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            using (var userUsage = await GetOrCreateLocalUserState())
            {
                Debug.Assert(userUsage.Item != null);

                using (var roomUsage = await getLocalUserRoom(userUsage.Item))
                {
                    var room = roomUsage.Item;

                    if (room == null)
                        throw new InvalidOperationException("Attempted to operate on a null room");

                    ensureIsHost(room);

                    settings.Name = await chatFilters.FilterAsync(settings.Name);
                    await room.ChangeRoomSettings(settings);
                }
            }
        }

        protected override async Task CleanUpState(ItemUsage<MultiplayerClientState> state)
        {
            Debug.Assert(state.Item != null);

            await base.CleanUpState(state);
            await matchmakingQueueService.RemoveFromQueueAsync(state.Item);

            if (state.Item.CurrentRoomID != null)
            {
                using (var roomUsage = await getLocalUserRoom(state.Item))
                    await RoomController.LeaveRoom(state.Item, roomUsage);
            }
        }

        /// <summary>
        /// Ensure the local user is the host of the room, and throw if they are not.
        /// </summary>
        private void ensureIsHost(MultiplayerRoom room)
        {
            if (room.Host?.UserID != Context.GetUserId())
                throw new NotHostException();
        }

        /// <summary>
        /// Retrieve the <see cref="MultiplayerRoom"/> for the local context user.
        /// </summary>
        private async Task<ItemUsage<ServerMultiplayerRoom>> getLocalUserRoom(MultiplayerClientState state)
        {
            if (state.CurrentRoomID == null)
                throw new NotJoinedRoomException();

            return await GetRoom(state.CurrentRoomID.Value);
        }

        internal Task<ItemUsage<ServerMultiplayerRoom>> GetRoom(long roomId) => RoomController.GetRoom(roomId);
    }
}
