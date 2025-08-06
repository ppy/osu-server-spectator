// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace SampleMultiplayerClient
{
    public class MultiplayerClient : IMultiplayerClient, IMultiplayerServer
    {
        private readonly HubConnection connection;

        public readonly int UserID;

        public MultiplayerClient(HubConnection connection, int userId)
        {
            this.connection = connection;
            UserID = userId;

            // this is kind of SILLY
            // https://github.com/dotnet/aspnetcore/issues/15198
            connection.On<MultiplayerRoomState>(nameof(IMultiplayerClient.RoomStateChanged), ((IMultiplayerClient)this).RoomStateChanged);
            connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserJoined), ((IMultiplayerClient)this).UserJoined);
            connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserLeft), ((IMultiplayerClient)this).UserLeft);
            connection.On<int>(nameof(IMultiplayerClient.HostChanged), ((IMultiplayerClient)this).HostChanged);
            connection.On<MultiplayerRoomSettings>(nameof(IMultiplayerClient.SettingsChanged), ((IMultiplayerClient)this).SettingsChanged);
            connection.On<int, MultiplayerUserState>(nameof(IMultiplayerClient.UserStateChanged), ((IMultiplayerClient)this).UserStateChanged);
            connection.On<int, BeatmapAvailability>(nameof(IMultiplayerClient.UserBeatmapAvailabilityChanged), ((IMultiplayerClient)this).UserBeatmapAvailabilityChanged);
            connection.On(nameof(IMultiplayerClient.LoadRequested), ((IMultiplayerClient)this).LoadRequested);
            connection.On<GameplayAbortReason>(nameof(IMultiplayerClient.GameplayAborted), ((IMultiplayerClient)this).GameplayAborted);
            connection.On(nameof(IMultiplayerClient.GameplayStarted), ((IMultiplayerClient)this).GameplayStarted);
            connection.On(nameof(IMultiplayerClient.ResultsReady), ((IMultiplayerClient)this).ResultsReady);
            connection.On<int, IEnumerable<APIMod>>(nameof(IMultiplayerClient.UserModsChanged), ((IMultiplayerClient)this).UserModsChanged);
            connection.On<MatchRoomState>(nameof(IMultiplayerClient.MatchRoomStateChanged), ((IMultiplayerClient)this).MatchRoomStateChanged);
            connection.On<int, MatchUserState>(nameof(IMultiplayerClient.MatchUserStateChanged), ((IMultiplayerClient)this).MatchUserStateChanged);
            connection.On<MatchServerEvent>(nameof(IMultiplayerClient.MatchEvent), ((IMultiplayerClient)this).MatchEvent);
            connection.On<MultiplayerPlaylistItem>(nameof(IMultiplayerClient.PlaylistItemAdded), ((IMultiplayerClient)this).PlaylistItemAdded);
            connection.On<MultiplayerPlaylistItem>(nameof(IMultiplayerClient.PlaylistItemChanged), ((IMultiplayerClient)this).PlaylistItemChanged);
            connection.On<long>(nameof(IMultiplayerClient.PlaylistItemRemoved), ((IMultiplayerClient)this).PlaylistItemRemoved);
            connection.On<int, long, string>(nameof(IMultiplayerClient.Invited), ((IMultiplayerClient)this).Invited);
            connection.On(nameof(IStatefulUserHubClient.DisconnectRequested), ((IStatefulUserHubClient)this).DisconnectRequested);
        }

        public MultiplayerUserState State { get; private set; }

        public MatchUserState? MatchState { get; private set; }

        public MatchServerEvent? LastMatchEvent { get; set; }

        public BeatmapAvailability BeatmapAvailability { get; private set; } = BeatmapAvailability.LocallyAvailable();

        public IEnumerable<APIMod> UserMods { get; private set; } = Enumerable.Empty<APIMod>();

        public int? UserBeatmap { get; private set; }

        public int? UserRuleset { get; private set; }

        public MultiplayerRoom? Room { get; private set; }

        public Task<MultiplayerRoom> CreateRoom(MultiplayerRoom room)
            => throw new NotImplementedException();

        public async Task<MultiplayerRoom> JoinRoom(long roomId)
            => await JoinRoomWithPassword(roomId, string.Empty);

        public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string? password = null)
            => Room = await connection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerServer.JoinRoomWithPassword), roomId, password ?? string.Empty);

        public Task ToggleMatchmakingQueue()
        {
            throw new NotImplementedException();
        }

        public async Task LeaveRoom()
        {
            await connection.InvokeAsync(nameof(IMultiplayerServer.LeaveRoom));
            Room = null;
        }

        public Task TransferHost(int userId) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.TransferHost), userId);

        public Task KickUser(int userId) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.KickUser), userId);

        public Task ChangeSettings(MultiplayerRoomSettings settings) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeSettings), settings);

        public Task ChangeState(MultiplayerUserState newState) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeState), newState);

        public Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeBeatmapAvailability), newBeatmapAvailability);

        public Task ChangeUserStyle(int? beatmapId, int? rulesetId) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeUserStyle), beatmapId, rulesetId);

        public Task ChangeUserMods(IEnumerable<APIMod> newMods) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeUserMods), newMods);

        public Task SendMatchRequest(MatchUserRequest request) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.SendMatchRequest), request);

        public Task StartMatch() =>
            connection.InvokeAsync(nameof(IMultiplayerServer.StartMatch));

        public Task AbortMatch() =>
            connection.InvokeAsync(nameof(IMultiplayerServer.AbortMatch));

        public Task AbortGameplay() =>
            connection.InvokeAsync(nameof(IMultiplayerServer.AbortGameplay));

        public Task AddPlaylistItem(MultiplayerPlaylistItem item) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.AddPlaylistItem), item);

        public Task EditPlaylistItem(MultiplayerPlaylistItem item) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.EditPlaylistItem), item);

        public Task RemovePlaylistItem(long playlistItemId) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.RemovePlaylistItem), playlistItemId);

        public Task InvitePlayer(int userId)
            => connection.InvokeAsync(nameof(IMultiplayerServer.InvitePlayer), userId);

        Task IMultiplayerClient.RoomStateChanged(MultiplayerRoomState state)
        {
            Debug.Assert(Room != null);
            Room.State = state;

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.UserJoined(MultiplayerRoomUser user)
        {
            Debug.Assert(Room != null);
            Room.Users.Add(user);

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.UserLeft(MultiplayerRoomUser user)
        {
            Debug.Assert(Room != null);
            Room.Users.Remove(user);

            return Task.CompletedTask;
        }

        public Task UserKicked(MultiplayerRoomUser user) => ((IMultiplayerClient)this).UserLeft(user);

        Task IMultiplayerClient.Invited(int invitedBy, long roomID, string password)
        {
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.HostChanged(int userId)
        {
            Debug.Assert(Room != null);
            Room.Host = Room.Users.FirstOrDefault(u => u.UserID == userId);

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.SettingsChanged(MultiplayerRoomSettings newSettings)
        {
            Debug.Assert(Room != null);
            Room.Settings = newSettings;

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.UserStateChanged(int userId, MultiplayerUserState state)
        {
            if (userId == UserID)
                State = state;

            return Task.CompletedTask;
        }

        public Task MatchUserStateChanged(int userId, MatchUserState state)
        {
            if (userId == UserID)
                MatchState = state;

            return Task.CompletedTask;
        }

        public Task MatchRoomStateChanged(MatchRoomState state)
        {
            Debug.Assert(Room != null);
            Room.MatchState = state;

            return Task.CompletedTask;
        }

        public Task MatchEvent(MatchServerEvent e)
        {
            Console.WriteLine($"Match event received {e}");
            LastMatchEvent = e;

            return Task.CompletedTask;
        }

        public Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
        {
            if (userId == UserID)
                BeatmapAvailability = beatmapAvailability;

            return Task.CompletedTask;
        }

        public Task UserStyleChanged(int userId, int? beatmapId, int? rulesetId)
        {
            if (userId == UserID)
            {
                UserBeatmap = beatmapId;
                UserRuleset = rulesetId;
            }

            return Task.CompletedTask;
        }

        public Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
        {
            if (userId == UserID)
                UserMods = mods;

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.LoadRequested()
        {
            Console.WriteLine($"User {UserID} was requested to load");
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.GameplayAborted(GameplayAbortReason reason)
        {
            Console.WriteLine($"User {UserID} gameplay was aborted ({reason})");
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.GameplayStarted()
        {
            Console.WriteLine($"User {UserID} was informed the game started");
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.ResultsReady()
        {
            Console.WriteLine($"User {UserID} was informed the results are ready");
            return Task.CompletedTask;
        }

        public Task PlaylistItemAdded(MultiplayerPlaylistItem item)
        {
            Console.WriteLine($"Playlist item added (beatmap: {item.BeatmapID}, ruleset: {item.RulesetID})");
            return Task.CompletedTask;
        }

        public Task PlaylistItemRemoved(long playlistItemId)
        {
            Console.WriteLine($"Playlist item removed (id: {playlistItemId})");
            return Task.CompletedTask;
        }

        public Task PlaylistItemChanged(MultiplayerPlaylistItem item)
        {
            Console.WriteLine($"Playlist item changed (id: {item.ID} beatmap: {item.BeatmapID}, ruleset: {item.RulesetID})");
            return Task.CompletedTask;
        }

        public Task MatchmakingQueueStatusChanged(MatchmakingQueueStatus? status)
        {
            throw new NotImplementedException();
        }

        public Task MatchmakingSelectionToggled(int userId, long playlistItemId)
        {
            throw new NotImplementedException();
        }

        public Task MatchmakingToggleSelection(long playlistItemId)
        {
            throw new NotImplementedException();
        }

        public async Task DisconnectRequested()
        {
            Console.WriteLine("Disconnect requested");
            await LeaveRoom();
        }
    }
}
