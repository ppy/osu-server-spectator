// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Server.Spectator.Storage
{
    public class FileScoreStorage : IScoreStorage
    {
        private readonly ILogger logger;

        public FileScoreStorage(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger(nameof(FileScoreStorage));
        }

        public Task WriteAsync(Score score)
        {
            var legacyEncoder = new LegacyScoreEncoder(score, null);

            string filename = score.ScoreInfo.OnlineID.ToString();

            logger.LogInformation("Writing replay for score {scoreId} to {filename}",
                score.ScoreInfo.OnlineID,
                filename);

            using (var outStream = File.Create(Path.Combine(AppSettings.ReplaysPath, filename)))
                legacyEncoder.Encode(outStream);

            return Task.CompletedTask;
        }
    }
}
