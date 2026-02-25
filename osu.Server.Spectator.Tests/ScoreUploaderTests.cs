// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Storage;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    [Collection("Tests that adjust global AppSettings")]
    public class ScoreUploaderTests
    {
        private readonly Mock<IDatabaseAccess> mockDatabase;
        private readonly Mock<IScoreStorage> mockStorage;
        private readonly Mock<IDatabaseFactory> databaseFactory;
        private readonly Mock<ILoggerFactory> loggerFactory;
        private readonly IMemoryCache memoryCache;

        public ScoreUploaderTests()
        {
            mockDatabase = new Mock<IDatabaseAccess>();
            mockDatabase.Setup(db => db.GetScoreFromTokenAsync(1)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 2,
                passed = true
            }));

            databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);

            loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                         .Returns(new Mock<ILogger>().Object);

            mockStorage = new Mock<IScoreStorage>();

            memoryCache = new MemoryCache(new MemoryCacheOptions());
        }

        /// <summary>
        /// Currently the replay upload process deals with two sources of score data.
        /// One is local to the spectator server and created in `SpectatorHub.BeginPlaySession()`.
        /// Among others, it contains the username of the player, which in the legacy replay format
        /// is the only piece of information that links the replay to the player.
        /// The other source is the database. This source will contain the online ID and passed state of the score,
        /// which will not be available in the local instance.
        /// This test ensures that the two are merged correctly in order to not drop any important data.
        /// </summary>
        [Fact]
        public async Task ScoreDataMergedCorrectly()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            await uploader.EnqueueAsync(1, new Score
            {
                ScoreInfo =
                {
                    User = new APIUser
                    {
                        Id = 1234,
                        Username = "some user",
                    }
                    // note OnlineID and Passed not set.
                }
            }, new database_beatmap());

            await uploadsCompleteAsync(uploader);

            mockStorage.Verify(s => s.WriteAsync(
                It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 2
                                                        && item.Score.ScoreInfo.Passed
                                                        && item.Score.ScoreInfo.User.Username == "some user")), Times.Once);
        }

        [Fact]
        public async Task ScoreUploads()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            await uploader.EnqueueAsync(1, new Score(), new database_beatmap());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 2)), Times.Once);

            await uploader.EnqueueAsync(1, new Score(), new database_beatmap());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 2)), Times.Exactly(2));
        }

        [Fact]
        public async Task ScoreDoesNotUploadIfDisabled()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = false
            };

            await uploader.EnqueueAsync(1, new Score(), new database_beatmap());
            await Task.Delay(1000);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()), Times.Never);
        }

        [Fact]
        public async Task ScoreUploadsWithDelayedScoreToken()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            // Score with no token.
            await uploader.EnqueueAsync(2, new Score(), new database_beatmap());
            await Task.Delay(1000);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()), Times.Never);

            // Give the score a token.
            mockDatabase.Setup(db => db.GetScoreFromTokenAsync(2)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 3,
                passed = true
            }));

            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 3)), Times.Once);
        }

        [Fact]
        public async Task TimedOutScoreDoesNotUpload()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            uploader.TimeoutInterval = 100;

            // Score with no token.
            await uploader.EnqueueAsync(2, new Score(), new database_beatmap());
            Thread.Sleep(1000); // Wait for cancellation.
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()), Times.Never);

            // Give the score a token now. It should still not upload because it has timed out.
            mockDatabase.Setup(db => db.GetScoreFromTokenAsync(2)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 3,
                passed = true
            }));
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()), Times.Never);

            // New score that has a token (ensure the loop keeps running).
            mockDatabase.Setup(db => db.GetScoreFromTokenAsync(3)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 4,
                passed = true
            }));
            await uploader.EnqueueAsync(3, new Score(), new database_beatmap());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()), Times.Once);
            mockStorage.Verify(s => s.WriteAsync(It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 4)), Times.Once);
        }

        [Fact]
        public async Task FailedUploadDoesRetryAndFail()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true,
                TimeoutInterval = 1000,
            };

            mockStorage.Setup(storage => storage.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()))
                       .Callback<ScoreUploader.UploadItem>(_ => throw new InvalidOperationException());

            await uploader.EnqueueAsync(1, new Score(), new database_beatmap());

            // Things are failing badly, exceptions that don't resolve.
            // We expect the upload to be dropped after the `TimeoutInterval` in such a case.
            await uploadsCompleteAsync(uploader);
        }

        [Fact]
        public async Task FailedUploadDoesRetryAndSucceed()
        {
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            bool shouldThrow = true;
            int uploadCount = 0;
            ManualResetEventSlim failed = new ManualResetEventSlim();

            mockStorage.Setup(storage => storage.WriteAsync(It.IsAny<ScoreUploader.UploadItem>()))
                       .Callback<ScoreUploader.UploadItem>(_ =>
                       {
                           // ReSharper disable once AccessToModifiedClosure
                           if (shouldThrow)
                           {
                               failed.Set();
                               throw new InvalidOperationException();
                           }

                           uploadCount++;
                       });

            // Throwing score.
            await uploader.EnqueueAsync(1, new Score(), new database_beatmap());

            failed.Wait();
            Assert.Equal(0, uploadCount);

            shouldThrow = false;

            await uploadsCompleteAsync(uploader);
            Assert.Equal(1, uploadCount);
        }

        [Fact]
        public async Task TestMassUploads()
        {
            AppSettings.ReplayUploaderConcurrency = 4;
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object, memoryCache)
            {
                SaveReplays = true
            };

            for (int i = 0; i < 1000; ++i)
                await uploader.EnqueueAsync(1, new Score(), new database_beatmap());

            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<ScoreUploader.UploadItem>(item => item.Score.ScoreInfo.OnlineID == 2)), Times.Exactly(1000));
            AppSettings.ReplayUploaderConcurrency = 1;
        }

        private async Task uploadsCompleteAsync(ScoreUploader uploader, int attempts = 5)
        {
            while (uploader.RemainingUsages > 0)
            {
                if (attempts <= 0)
                    Assert.Fail("Waiting for score upload to proceed timed out");

                attempts -= 1;
                await Task.Delay(1000);
            }
        }
    }
}
