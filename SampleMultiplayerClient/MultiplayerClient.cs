// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Game.Online.API;
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
            connection.On(nameof(IMultiplayerClient.MatchStarted), ((IMultiplayerClient)this).MatchStarted);
            connection.On(nameof(IMultiplayerClient.ResultsReady), ((IMultiplayerClient)this).ResultsReady);
            connection.On<int, IEnumerable<APIMod>>(nameof(IMultiplayerClient.UserModsChanged), ((IMultiplayerClient)this).UserModsChanged);
            connection.On<MatchRoomState>(nameof(IMultiplayerClient.MatchRoomStateChanged), ((IMultiplayerClient)this).MatchRoomStateChanged);
            connection.On<int, MatchUserState>(nameof(IMultiplayerClient.MatchUserStateChanged), ((IMultiplayerClient)this).MatchUserStateChanged);
        }

        public MultiplayerUserState State { get; private set; }

        public BeatmapAvailability BeatmapAvailability { get; private set; } = BeatmapAvailability.LocallyAvailable();

        public IEnumerable<APIMod> UserMods { get; private set; } = Enumerable.Empty<APIMod>();

        public MultiplayerRoom? Room { get; private set; }

        public async Task<MultiplayerRoom> JoinRoom(long roomId)
        {
            return await JoinRoomWithPassword(roomId, string.Empty);
        }

        public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string? password = null)
        {
            return Room = await connection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerServer.JoinRoomWithPassword), roomId, password ?? string.Empty);
        }

        public async Task LeaveRoom()
        {
            await connection.InvokeAsync(nameof(IMultiplayerServer.LeaveRoom));
            Room = null;
        }

        public Task TransferHost(int userId) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.TransferHost), userId);

        public Task ChangeSettings(MultiplayerRoomSettings settings) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeSettings), settings);

        public Task ChangeState(MultiplayerUserState newState) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeState), newState);

        public Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeBeatmapAvailability), newBeatmapAvailability);

        public Task ChangeUserMods(IEnumerable<APIMod> newMods) =>
            connection.InvokeAsync(nameof(IMultiplayerServer.ChangeUserMods), newMods);

        public Task SendMatchRequest(MatchUserRequest request)
        {
            throw new NotImplementedException();
        }

        public Task StartMatch() =>
            connection.InvokeAsync(nameof(IMultiplayerServer.StartMatch));

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
            if (userId == this.UserID)
                State = state;

            return Task.CompletedTask;
        }

        public Task MatchUserStateChanged(int userId, MatchUserState state)
        {
            throw new NotImplementedException();
        }

        public Task MatchRoomStateChanged(MatchRoomState state)
        {
            throw new NotImplementedException();
        }

        public Task MatchEvent(MatchServerEvent e)
        {
            throw new NotImplementedException();
        }

        public Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
        {
            if (userId == this.UserID)
                BeatmapAvailability = beatmapAvailability;

            return Task.CompletedTask;
        }

        public Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
        {
            if (userId == this.UserID)
                UserMods = mods;

            return Task.CompletedTask;
        }

        Task IMultiplayerClient.LoadRequested()
        {
            Console.WriteLine($"User {UserID} was requested to load");
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.MatchStarted()
        {
            Console.WriteLine($"User {UserID} was informed the game started");
            return Task.CompletedTask;
        }

        Task IMultiplayerClient.ResultsReady()
        {
            Console.WriteLine($"User {UserID} was informed the results are ready");
            return Task.CompletedTask;
        }
    }
}
