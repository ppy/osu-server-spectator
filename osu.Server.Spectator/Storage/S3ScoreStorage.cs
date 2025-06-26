// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Scoring.Legacy;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Storage
{
    public class S3ScoreStorage : IScoreStorage
    {
        private readonly ILogger logger;

        public S3ScoreStorage(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger(nameof(S3ScoreStorage));
        }

        public async Task WriteAsync(ScoreUploader.UploadItem item)
        {
            using (var outStream = new MemoryStream())
            {
                var score = item.Score;
                // beatmap version is required for correct encoding of replays for beatmaps with version <5
                // (see `LegacyBeatmapDecoder.EARLY_VERSION_TIMING_OFFSET`).
                new LegacyScoreEncoder(score, new Beatmap { BeatmapVersion = item.Beatmap.osu_file_version }).Encode(outStream, true);

                outStream.Seek(0, SeekOrigin.Begin);

                logger.LogInformation($"Uploading replay for score {score.ScoreInfo.OnlineID}");

                await S3.Upload(AppSettings.ReplaysBucket, score.ScoreInfo.OnlineID.ToString(CultureInfo.InvariantCulture), outStream, outStream.Length);
            }
        }
    }
}
