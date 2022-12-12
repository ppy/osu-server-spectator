// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Scoring;

namespace osu.Server.Spectator.Hubs
{
    public interface IScoreUploader
    {
        void Enqueue(long token, Score score);
    }
}
