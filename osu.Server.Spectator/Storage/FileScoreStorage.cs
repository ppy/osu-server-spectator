// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Server.Spectator.Storage
{
    public class FileScoreStorage : IScoreStorage
    {
        public Task WriteAsync(Score score)
        {
            var legacyEncoder = new LegacyScoreEncoder(score, null);

            string filename = score.ScoreInfo.OnlineID.ToString();

            Console.WriteLine($"Writing replay for score {score.ScoreInfo.OnlineID} to {filename}");

            using (var outStream = File.Create(Path.Combine(AppSettings.ReplaysPath, filename)))
                legacyEncoder.Encode(outStream);

            return Task.CompletedTask;
        }
    }
}
