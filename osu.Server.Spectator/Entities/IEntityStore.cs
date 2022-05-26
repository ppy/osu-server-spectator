// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Entities;

public interface IEntityStore
{
    /// <summary>
    /// Number of entities remaining in use.
    /// </summary>
    int RemainingUsages { get; }

    /// <summary>
    /// A display name for the managed entity.
    /// </summary>
    string EntityName { get; }

    /// <summary>
    /// Inform this entity store that a server shutdown transition is in progress, and new entities should not be allowed.
    /// </summary>
    void StopAcceptingEntities();
}
