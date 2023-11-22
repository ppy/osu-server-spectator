// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs.Metadata;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class BatchMetadataUpdaterTests
    {
        private readonly EntityStore<MetadataClientState> metadataStateStore;
        private readonly Mock<IDatabaseAccess> mockDatabase;
        private readonly BatchMetadataUpdater batchMetadataUpdater;

        public BatchMetadataUpdaterTests()
        {
            metadataStateStore = new EntityStore<MetadataClientState>();
            mockDatabase = new Mock<IDatabaseAccess>();

            var databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);

            batchMetadataUpdater = new BatchMetadataUpdater(databaseFactory.Object, metadataStateStore);
            batchMetadataUpdater.BatchInterval = 100;
            batchMetadataUpdater.BatchSize = 2;
        }

        [Fact]
        public async Task LastVisitTimeUpdatedCorrectly()
        {
            const int user_id = 1234;

            using (var usage = await metadataStateStore.GetForUse(user_id, true))
                usage.Item = new MetadataClientState(Guid.NewGuid().ToString(), user_id);

            await Task.Delay(200);
            mockDatabase.Verify(db => db.UpdateLastVisitTimeToNowAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == user_id)), Times.AtLeastOnce);

            using (var usage = await metadataStateStore.GetForUse(user_id))
                usage.Destroy();

            mockDatabase.Invocations.Clear();

            await Task.Delay(200);
            mockDatabase.Verify(db => db.UpdateLastVisitTimeToNowAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == user_id)), Times.Never);
        }

        [Fact]
        public async Task TestBatching()
        {
            int[] userIds = { 1234, 5678, 9012 };

            foreach (int userId in userIds)
            {
                using (var usage = await metadataStateStore.GetForUse(userId, true))
                    usage.Item = new MetadataClientState(Guid.NewGuid().ToString(), userId);
            }

            await Task.Delay(150);
            mockDatabase.Verify(db => db.UpdateLastVisitTimeToNowAsync(
                It.IsAny<IEnumerable<int>>()), Times.Exactly(2));
        }
    }
}
