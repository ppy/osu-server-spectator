// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public abstract class UserHubEntities<TUserState>
        where TUserState : ClientState
    {
        public readonly EntityStore<TUserState> ActiveUsers = new EntityStore<TUserState>();
    }
}
