// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

namespace osu.Server.Spectator.Hubs
{
    public class MultiplayerHub : StatefulUserHub<IMultiplayerClient, MultiplayerClientState>, IMultiplayerServer
    {
        public MultiplayerHub([NotNull] IDistributedCache cache)
            : base(cache)
        {
        }
    }
}
