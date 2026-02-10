// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs.Multiplayer
{
    /// <summary>
    /// Contains the state of a multiplayer in a hub.
    /// </summary>
    /// <remarks>
    /// The reason this is an interface and not a plain class is that there are currently two types of multiplayer "user":
    /// an actual multiplayer player, that connects via the lazer client,
    /// and a referee, connecting via the referee hub / third-party referee client.
    /// Those users' states are:
    /// <list type="item">
    /// <item>stored in memory and tracked separately,</item>
    /// <item>subject to different concurrency and reconnection guarantees (players' states are expected to fully drop on disconnection, and referees' not).</item>
    /// </list>
    /// </remarks>
    public interface IMultiplayerUserState
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        int UserId { get; }

        /// <summary>
        /// Creates a <see cref="MultiplayerRoomUser"/> representing this client.
        /// </summary>
        MultiplayerRoomUser CreateRoomUser();

        /// <summary>
        /// Associates this <see cref="IMultiplayerUserState"/> with the given <paramref name="roomId"/>.
        /// Performed on room join.
        /// </summary>
        void AssociateWithRoom(long roomId);

        /// <summary>
        /// Whether this <see cref="IMultiplayerUserState"/> is currently associated with the given <paramref name="roomId"/>.
        /// </summary>
        bool IsAssociatedWithRoom(long roomId);

        /// <summary>
        /// Disassociates this <see cref="IMultiplayerUserState"/> from the given <paramref name="roomId"/>.
        /// Performed on room leave.
        /// </summary>
        void DisassociateFromRoom(long roomId);

        /// <summary>
        /// Subscribes to relevant event groups for the given <paramref name="roomId"/> via the supplied <paramref name="eventDispatcher"/>.
        /// </summary>
        Task SubscribeToEvents(MultiplayerEventDispatcher eventDispatcher, long roomId);

        /// <summary>
        /// Unsubscribes from relevant event groups for the given <paramref name="roomId"/> via the supplied <paramref name="eventDispatcher"/>.
        /// </summary>
        Task UnsubscribeFromEvents(MultiplayerEventDispatcher eventDispatcher, long roomId);
    }
}
