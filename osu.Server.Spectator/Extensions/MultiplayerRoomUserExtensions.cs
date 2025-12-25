// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Extensions
{
    public static class MultiplayerRoomUserExtensions
    {
        /// <summary>
        /// Whether a user is in a state capable of starting gameplay.
        /// </summary>
        public static bool IsReadyForGameplay(this MultiplayerRoomUser user)
            => user.BeatmapAvailability.State == DownloadState.LocallyAvailable && (user.State == MultiplayerUserState.Ready || user.State == MultiplayerUserState.Idle);
    }
}
