// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public interface IMultiplayerServerMatchRulesetCallbacks
    {
        Task UpdateMatchRulesetRoomState(MultiplayerRoom room);

        Task UpdateMatchRulesetUserState(MultiplayerRoom room, MultiplayerRoomUser user);
    }
}
