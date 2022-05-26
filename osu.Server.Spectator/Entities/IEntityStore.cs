// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Entities;

public interface IEntityStore
{
    bool AnyRemainingUsage { get; }

    void StopAcceptingEntities();
}
