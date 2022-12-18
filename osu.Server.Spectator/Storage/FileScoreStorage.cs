// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Server.Spectator.Hubs;

namespace osu.Server.Spectator.Storage
{
    public class FileScoreStorage : IScoreStorage
    {
        public Task WriteAsync(Score score)
        {
            var scoreInfo = score.ScoreInfo;
            var legacyEncoder = new LegacyScoreEncoder(score, null);

            string path = Path.Combine(SpectatorHub.REPLAYS_PATH, scoreInfo.Date.Year.ToString(), scoreInfo.Date.Month.ToString(), scoreInfo.Date.Day.ToString());

            Directory.CreateDirectory(path);

            string filename = $"replay-{scoreInfo.Ruleset.ShortName}_{scoreInfo.BeatmapInfo.OnlineID}_{score.ScoreInfo.OnlineID}.osr";

            Console.WriteLine($"Writing replay for score {score.ScoreInfo.OnlineID} to {filename}");

            using (var outStream = File.Create(Path.Combine(path, filename)))
                legacyEncoder.Encode(outStream);

            return Task.CompletedTask;
        }
    }
}
