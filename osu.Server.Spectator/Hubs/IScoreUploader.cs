// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Scoring;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public interface IScoreUploader : IEntityStore
    {
        void Enqueue(long token, Score score);
    }
}
