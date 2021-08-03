// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Hubs
{
    public interface IMultiplayerServerMatchCallbacks
    {
        Task SendMatchEvent(MultiplayerRoom room, MatchServerEvent e);

        Task UpdateMatchRoomState(MultiplayerRoom room);

        Task UpdateMatchUserState(MultiplayerRoom room, MultiplayerRoomUser user);
    }
}
