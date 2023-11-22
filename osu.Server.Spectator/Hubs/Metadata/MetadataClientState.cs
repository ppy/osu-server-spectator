// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class MetadataClientState : ClientState
    {
        // TODO: user activity information
        // TODO: build hash

        public MetadataClientState(in string connectionId, in int userId)
            : base(in connectionId, in userId)
        {
        }
    }
}
