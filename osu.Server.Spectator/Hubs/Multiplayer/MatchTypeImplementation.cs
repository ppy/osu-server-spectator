// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    public abstract class MatchTypeImplementation
    {
        public abstract IMultiplayerQueue Queue { get; }

        protected readonly ServerMultiplayerRoom Room;
        protected readonly IMultiplayerHubContext Hub;

        protected MatchTypeImplementation(ServerMultiplayerRoom room, IMultiplayerHubContext hub)
        {
            Room = room;
            Hub = hub;
        }

        /// <summary>
        /// Invoked when this <see cref="MatchTypeImplementation"/> is constructed to perform any initialisation.
        /// </summary>
        public virtual async Task Initialise()
        {
            await Queue.Initialise();
        }

        /// <summary>
        /// Called when a user has requested a match type specific action.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="request">The nature of the action.</param>
        public virtual Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called once for each user which joins the room. Will be run once for each user after initial construction.
        /// </summary>
        /// <param name="user">The user which joined the room.</param>
        public virtual Task HandleUserJoined(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called once for each user leaving the room.
        /// </summary>
        /// <param name="user">The user which left the room.</param>
        public virtual Task HandleUserLeft(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleUserBeatmapAvailabilityChanged(MultiplayerRoomUser user, BeatmapAvailability availability)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleMatchComplete()
        {
            return Task.CompletedTask;
        }

        public abstract MatchStartedEventDetail GetMatchDetails();
    }
}
