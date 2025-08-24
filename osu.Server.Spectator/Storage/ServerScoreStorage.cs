using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Scoring.Legacy;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Storage
{
    public class ServerScoreStorage : IScoreStorage
    {
        private readonly ILogger logger;
        private readonly ISharedInterop sharedInterop;

        public ServerScoreStorage(ILoggerFactory loggerFactory, ISharedInterop sharedInterop)
        {
            this.sharedInterop = sharedInterop;
            logger = loggerFactory.CreateLogger(nameof(ServerScoreStorage));
        }

        public Task WriteAsync(ScoreUploader.UploadItem item)
        {
            var score = item.Score;
            // beatmap version is required for correct encoding of replays for beatmaps with version <5
            // (see `LegacyBeatmapDecoder.EARLY_VERSION_TIMING_OFFSET`).
            var legacyEncoder = new LegacyScoreEncoder(score, new Beatmap { BeatmapVersion = item.Beatmap.osu_file_version });

            using (var outStream = new MemoryStream())
            {
                legacyEncoder.Encode(outStream, true);
                sharedInterop.UploadReplayAsync(score.ScoreInfo.UserID, score.ScoreInfo.OnlineID, item.Beatmap.beatmap_id, outStream);
            }

            return Task.CompletedTask;
        }
    }
}