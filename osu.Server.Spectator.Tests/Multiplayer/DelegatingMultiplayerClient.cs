// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    /// <summary>
    /// Used in testing. Delegates calls to one or more <see cref="IMultiplayerClient"/>s.
    /// Note: All members must be virtual!!
    /// </summary>
    public class DelegatingMultiplayerClient : IMultiplayerClient, IClientProxy
    {
        private readonly IEnumerable<IMultiplayerClient> clients;

        public DelegatingMultiplayerClient(IEnumerable<IMultiplayerClient> clients)
        {
            this.clients = clients;
        }

        public virtual async Task RoomStateChanged(MultiplayerRoomState state)
        {
            foreach (var c in clients)
                await c.RoomStateChanged(state);
        }

        public virtual async Task UserJoined(MultiplayerRoomUser user)
        {
            foreach (var c in clients)
                await c.UserJoined(user);
        }

        public virtual async Task UserLeft(MultiplayerRoomUser user)
        {
            foreach (var c in clients)
                await c.UserLeft(user);
        }

        public virtual async Task UserKicked(MultiplayerRoomUser user)
        {
            foreach (var c in clients)
                await c.UserKicked(user);
        }

        public virtual async Task HostChanged(int userId)
        {
            foreach (var c in clients)
                await c.HostChanged(userId);
        }

        public virtual async Task SettingsChanged(MultiplayerRoomSettings newSettings)
        {
            foreach (var c in clients)
                await c.SettingsChanged(newSettings);
        }

        public virtual async Task UserStateChanged(int userId, MultiplayerUserState state)
        {
            foreach (var c in clients)
                await c.UserStateChanged(userId, state);
        }

        public virtual async Task MatchUserStateChanged(int userId, MatchUserState state)
        {
            foreach (var c in clients)
                await c.MatchUserStateChanged(userId, state);
        }

        public virtual async Task MatchRoomStateChanged(MatchRoomState state)
        {
            foreach (var c in clients)
                await c.MatchRoomStateChanged(state);
        }

        public virtual async Task MatchEvent(MatchServerEvent e)
        {
            foreach (var c in clients)
                await c.MatchEvent(e);
        }

        public virtual async Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
        {
            foreach (var c in clients)
                await c.UserBeatmapAvailabilityChanged(userId, beatmapAvailability);
        }

        public virtual async Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
        {
            foreach (var c in clients)
                await c.UserModsChanged(userId, mods);
        }

        public virtual async Task LoadRequested()
        {
            foreach (var c in clients)
                await c.LoadRequested();
        }

        public virtual async Task LoadAborted()
        {
            foreach (var c in clients)
                await c.LoadAborted();
        }

        public virtual async Task GameplayStarted()
        {
            foreach (var c in clients)
                await c.GameplayStarted();
        }

        public virtual async Task ResultsReady()
        {
            foreach (var c in clients)
                await c.ResultsReady();
        }

        public virtual async Task PlaylistItemAdded(MultiplayerPlaylistItem item)
        {
            foreach (var c in clients)
                await c.PlaylistItemAdded(item);
        }

        public virtual async Task PlaylistItemRemoved(long playlistItemId)
        {
            foreach (var c in clients)
                await c.PlaylistItemRemoved(playlistItemId);
        }

        public virtual async Task PlaylistItemChanged(MultiplayerPlaylistItem item)
        {
            foreach (var c in clients)
                await c.PlaylistItemChanged(item);
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = new CancellationToken())
        {
            return (Task)GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!.Invoke(this, args)!;
        }
    }
}
