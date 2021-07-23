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
    public class ServerMultiplayerRoom : MultiplayerRoom, IMultiplayerServerMatchRulesetCallbacks
    {
        private readonly IMultiplayerServerMatchRulesetCallbacks hubCallbacks;

        private MatchRuleset matchRuleset;

        [UsedImplicitly]
        private readonly BindableList<MultiplayerRoomUser> bindableUsers;

        public MatchRuleset MatchRuleset
        {
            get => matchRuleset;
            set
            {
                if (matchRuleset == value)
                    return;

                matchRuleset = value;

                foreach (var u in Users)
                    matchRuleset.HandleUserJoined(u);
            }
        }

        public ServerMultiplayerRoom(long roomId, IMultiplayerServerMatchRulesetCallbacks hubCallbacks)
            : base(roomId)
        {
            this.hubCallbacks = hubCallbacks;
            matchRuleset = new HeadToHeadRuleset(this);

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
                        matchRuleset.HandleUserJoined(u);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Debug.Assert(e.OldItems != null);

                    foreach (var u in e.OldItems.Cast<MultiplayerRoomUser>())
                        matchRuleset.HandleUserLeft(u);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public Task SendMatchRulesetEvent(MultiplayerRoom room, MatchRulesetServerEvent e) => hubCallbacks.SendMatchRulesetEvent(room, e);

        public Task UpdateMatchRulesetRoomState(MultiplayerRoom room) => hubCallbacks.UpdateMatchRulesetRoomState(room);

        public Task UpdateMatchRulesetUserState(MultiplayerRoom room, MultiplayerRoomUser user) => hubCallbacks.UpdateMatchRulesetUserState(room, user);
    }
}
