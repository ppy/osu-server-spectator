// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Entities
{
    public abstract class UserHubEntities<TUserState>
        where TUserState : ClientState
    {
        public readonly EntityStore<TUserState> Users = new EntityStore<TUserState>();
    }
}
