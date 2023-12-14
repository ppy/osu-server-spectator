// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Moq;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Metadata;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class BuildUserCountUpdaterTest
    {
        private readonly EntityStore<MetadataClientState> clientStates;
        private readonly Mock<IDatabaseFactory> databaseFactoryMock;
        private readonly Mock<IDatabaseAccess> databaseAccessMock;

        public BuildUserCountUpdaterTest()
        {
            clientStates = new EntityStore<MetadataClientState>();

            databaseFactoryMock = new Mock<IDatabaseFactory>();
            databaseAccessMock = new Mock<IDatabaseAccess>();
            databaseFactoryMock.Setup(df => df.GetInstance()).Returns(databaseAccessMock.Object);
        }

        [Fact]
        public async Task TestPeriodicUpdates()
        {
            databaseAccessMock.Setup(db => db.GetAllMainLazerBuildsAsync())
                              .ReturnsAsync(new[]
                              {
                                  new osu_build { build_id = 1, hash = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, users = 0, version = "2023.1208.0" },
                                  new osu_build { build_id = 2, hash = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, users = 0, version = "2023.1209.0" }
                              });

            await trackUser(1, "cafebabe");
            await trackUser(2, "cafebabe");
            await trackUser(3, "deadbeef");
            await trackUser(4, "cafebabe");
            await trackUser(5, "deadbeef");
            await trackUser(6, "unknown");

            AppSettings.TrackBuildUserCounts = true;
            var updater = new BuildUserCountUpdater(clientStates, databaseFactoryMock.Object)
            {
                UpdateInterval = 50
            };
            await Task.Delay(100);

            databaseAccessMock.Verify(db => db.UpdateBuildUserCountAsync(It.Is<osu_build>(build => build.build_id == 1 && build.users == 3)), Times.AtLeastOnce);
            databaseAccessMock.Verify(db => db.UpdateBuildUserCountAsync(It.Is<osu_build>(build => build.build_id == 2 && build.users == 2)), Times.AtLeastOnce);

            await disconnectUser(3);
            await disconnectUser(5);
            await Task.Delay(100);

            databaseAccessMock.Verify(db => db.UpdateBuildUserCountAsync(It.Is<osu_build>(build => build.build_id == 2 && build.users == 0)), Times.AtLeastOnce);

            updater.Dispose();
        }

        private async Task trackUser(int userId, string versionHash)
        {
            using (var usage = await clientStates.GetForUse(userId, true))
                usage.Item = new MetadataClientState(Guid.NewGuid().ToString(), userId, versionHash);
        }

        private async Task disconnectUser(int userId)
        {
            using (var usage = await clientStates.GetForUse(userId))
                usage.Destroy();
        }
    }
}
