// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Concurrent;
using osu.Game.Online.Metadata;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class MultiplayerRoomStats
    {
        public long RoomID { get; init; }

        public readonly ConcurrentDictionary<long, MultiplayerPlaylistItemStats> PlaylistItemStats = new ConcurrentDictionary<long, MultiplayerPlaylistItemStats>();
    }
}
