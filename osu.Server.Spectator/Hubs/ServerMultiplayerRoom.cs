// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Bindables;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Hubs
{
    public class ServerMultiplayerRoom : MultiplayerRoom
    {
        private readonly IMultiplayerServerMatchCallbacks hubCallbacks;

        [UsedImplicitly]
        private readonly BindableList<MultiplayerRoomUser> bindableUsers;

        private MatchTypeImplementation matchTypeImplementation;

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

            // just to ensure non-null.
            matchTypeImplementation = createTypeImplementation(MatchType.HeadToHead);

            Users = bindableUsers = new BindableList<MultiplayerRoomUser>();

            bindableUsers.BindCollectionChanged(usersChanged);
        }

        public void ChangeMatchType(MatchType type) => MatchTypeImplementation = createTypeImplementation(type);

        private MatchTypeImplementation createTypeImplementation(MatchType type)
        {
            switch (type)
            {
                case MatchType.TeamVersus:
                    return new TeamVersus(this, hubCallbacks);

                default:
                    return new HeadToHead(this, hubCallbacks);
            }
        }

        private void usersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Debug.Assert(e.NewItems != null);

                    foreach (var u in e.NewItems.Cast<MultiplayerRoomUser>())
                        MatchTypeImplementation.HandleUserJoined(u);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Debug.Assert(e.OldItems != null);

                    foreach (var u in e.OldItems.Cast<MultiplayerRoomUser>())
                        MatchTypeImplementation.HandleUserLeft(u);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
