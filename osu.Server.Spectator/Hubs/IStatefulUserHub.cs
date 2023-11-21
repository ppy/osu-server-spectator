// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Hubs
{
    /// <summary>
    /// Marker interface for <see cref="StatefulUserHub{TClient,TUserState}"/>.
    /// Allows bypassing generic constraints.
    /// </summary>
    public interface IStatefulUserHub
    {
    }
}
