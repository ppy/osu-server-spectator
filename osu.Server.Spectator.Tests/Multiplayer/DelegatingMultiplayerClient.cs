// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    /// <summary>
    /// Used in testing. Delegates calls to one or more <see cref="IMultiplayerClient"/>s.
    /// Note: All members must be virtual!!
    /// </summary>
    public class DelegatingMultiplayerClient : IMultiplayerClient, IMatchmakingClient, ISingleClientProxy
    {
        public virtual IEnumerable<IStatefulUserHubClient> Clients => Enumerable.Empty<IStatefulUserHubClient>();

        public virtual async Task RoomStateChanged(MultiplayerRoomState state)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.RoomStateChanged(state);
        }

        public virtual async Task UserJoined(MultiplayerRoomUser user)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserJoined(user);
        }

        public virtual async Task UserLeft(MultiplayerRoomUser user)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserLeft(user);
        }

        public virtual async Task UserKicked(MultiplayerRoomUser user)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserKicked(user);
        }

        public virtual async Task Invited(int invitedBy, long roomID, string password)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.Invited(invitedBy, roomID, password);
        }

        public virtual async Task HostChanged(int userId)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.HostChanged(userId);
        }

        public virtual async Task SettingsChanged(MultiplayerRoomSettings newSettings)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.SettingsChanged(newSettings);
        }

        public virtual async Task UserStateChanged(int userId, MultiplayerUserState state)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserStateChanged(userId, state);
        }

        public virtual async Task MatchUserStateChanged(int userId, MatchUserState state)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.MatchUserStateChanged(userId, state);
        }

        public virtual async Task MatchRoomStateChanged(MatchRoomState state)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.MatchRoomStateChanged(state);
        }

        public virtual async Task MatchEvent(MatchServerEvent e)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.MatchEvent(e);
        }

        public virtual async Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserBeatmapAvailabilityChanged(userId, beatmapAvailability);
        }

        public virtual async Task UserStyleChanged(int userId, int? beatmapId, int? rulesetId)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserStyleChanged(userId, beatmapId, rulesetId);
        }

        public virtual async Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserModsChanged(userId, mods);
        }

        public virtual async Task LoadRequested()
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.LoadRequested();
        }

        public virtual async Task GameplayAborted(GameplayAbortReason reason)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.GameplayAborted(reason);
        }

        public virtual async Task GameplayStarted()
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.GameplayStarted();
        }

        public virtual async Task ResultsReady()
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.ResultsReady();
        }

        public virtual async Task PlaylistItemAdded(MultiplayerPlaylistItem item)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.PlaylistItemAdded(item);
        }

        public virtual async Task PlaylistItemRemoved(long playlistItemId)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.PlaylistItemRemoved(playlistItemId);
        }

        public virtual async Task PlaylistItemChanged(MultiplayerPlaylistItem item)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.PlaylistItemChanged(item);
        }

        public virtual async Task UserVotedToSkipIntro(int userId)
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.UserVotedToSkipIntro(userId);
        }

        public virtual async Task VoteToSkipIntroPassed()
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.VoteToSkipIntroPassed();
        }

        public virtual async Task MatchmakingQueueJoined()
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingQueueJoined();
        }

        public virtual async Task MatchmakingQueueLeft()
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingQueueLeft();
        }

        public virtual async Task MatchmakingRoomInvited()
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingRoomInvited();
        }

        public virtual async Task MatchmakingRoomReady(long roomId, string password)
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingRoomReady(roomId, password);
        }

        public virtual async Task MatchmakingLobbyStatusChanged(MatchmakingLobbyStatus status)
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingLobbyStatusChanged(status);
        }

        public virtual async Task MatchmakingQueueStatusChanged(MatchmakingQueueStatus status)
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingQueueStatusChanged(status);
        }

        public virtual async Task MatchmakingItemSelected(int userId, long playlistItemId)
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingItemSelected(userId, playlistItemId);
        }

        public virtual async Task MatchmakingItemDeselected(int userId, long playlistItemId)
        {
            foreach (var c in Clients.OfType<IMatchmakingClient>())
                await c.MatchmakingItemDeselected(userId, playlistItemId);
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = new CancellationToken())
        {
            return (Task)GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!.Invoke(this, args)!;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken)
        {
            return (Task<T>)GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!.Invoke(this, args)!;
        }

        public async Task DisconnectRequested()
        {
            foreach (var c in Clients.OfType<IMultiplayerClient>())
                await c.DisconnectRequested();
        }
    }
}
