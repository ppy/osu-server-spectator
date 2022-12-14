// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Server.Spectator.Storage
{
    public class S3ScoreStorage : IScoreStorage
    {
        public async Task WriteAsync(Score score)
        {
            using (var outStream = new MemoryStream())
            {
                new LegacyScoreEncoder(score, null).Encode(outStream, true);

                outStream.Seek(0, SeekOrigin.Begin);

                Console.WriteLine($"Uploading replay for score {score.ScoreInfo.OnlineID}");

                await S3.Upload(AppSettings.ReplaysBucket, score.ScoreInfo.OnlineID.ToString(CultureInfo.InvariantCulture), outStream, outStream.Length);
            }
        }
    }
}
