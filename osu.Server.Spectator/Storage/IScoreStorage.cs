// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Storage
{
    public interface IScoreStorage
    {
        Task WriteAsync(ScoreUploader.UploadItem score);
    }
}
