// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Bindables;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class ServerMultiplayerRoom : MultiplayerRoom, IMultiplayerServerMatchCallbacks
    {
        private readonly IMultiplayerServerMatchCallbacks hubCallbacks;

        private MatchTypeImplementation matchTypeImplementation;

        [UsedImplicitly]
        private readonly BindableList<MultiplayerRoomUser> bindableUsers;

        public MatchTypeImplementation MatchTypeImplementation
        {
            get => matchTypeImplementation;
            set
            {
                if (matchTypeImplementation == value)
                    return;

                matchTypeImplementation = value;

                foreach (var u in Users)
                    matchTypeImplementation.HandleUserJoined(u);
            }
        }

        public ServerMultiplayerRoom(long roomId, IMultiplayerServerMatchCallbacks hubCallbacks)
            : base(roomId)
        {
            this.hubCallbacks = hubCallbacks;
            matchTypeImplementation = new HeadToHeadTypeImplementation(this);

            Users = bindableUsers = new BindableList<MultiplayerRoomUser>();

            bindableUsers.BindCollectionChanged(usersChanged);
        }

        private void usersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Debug.Assert(e.NewItems != null);

                    foreach (var u in e.NewItems.Cast<MultiplayerRoomUser>())
                        matchTypeImplementation.HandleUserJoined(u);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Debug.Assert(e.OldItems != null);

                    foreach (var u in e.OldItems.Cast<MultiplayerRoomUser>())
                        matchTypeImplementation.HandleUserLeft(u);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public Task SendMatchEvent(MultiplayerRoom room, MatchServerEvent e) => hubCallbacks.SendMatchEvent(room, e);

        public Task UpdateMatchRoomState(MultiplayerRoom room) => hubCallbacks.UpdateMatchRoomState(room);

        public Task UpdateMatchUserState(MultiplayerRoom room, MultiplayerRoomUser user) => hubCallbacks.UpdateMatchUserState(room, user);
    }
}
