// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class EntityStoreTests
    {
        private readonly EntityStore<TestItem, int> store;

        public EntityStoreTests()
        {
            store = new EntityStore<TestItem, int>();
        }

        [Fact]
        public async void TestGetTwiceRetainsItem()
        {
            using (var firstGet = await store.GetForUse(1))
            {
                firstGet.Item = new TestItem("test data");
            }

            using (var secondGet = await store.GetForUse(1))
            {
                Assert.NotNull(secondGet.Item);
                Assert.Equal("test data", secondGet.Item?.TestData);
            }
        }

        [Fact]
        public async void TestDestroyingTrackedEntity()
        {
            using (var firstGet = await store.GetForUse(1))
            {
                firstGet.Item = new TestItem("test data");
            }

            using (var secondGet = await store.GetForUse(1))
                Assert.NotNull(secondGet.Item);

            await store.Destroy(1);

            using (var thirdGet = await store.GetForUse(1))
                Assert.Null(thirdGet.Item);
        }

        [Fact]
        public async void TestGetTwiceWithDelayedReturn()
        {
            var firstLockAchieved = new ManualResetEventSlim();
            var firstLockDelayComplete = new ManualResetEventSlim();

            new Thread(async () =>
            {
                using (var firstGet = await store.GetForUse(1))
                {
                    // signal the second fetch to start once the first lock has been achieved.
                    firstLockAchieved.Set();

                    firstGet.Item = new TestItem("test data");

                    await Task.Delay(2000);

                    firstLockDelayComplete.Set();
                }
            }).Start();

            firstLockAchieved.Wait();

            // the delay should not be over yet.
            Assert.False(firstLockDelayComplete.IsSet);

            using (var secondGet = await store.GetForUse(1))
            {
                Assert.True(firstLockDelayComplete.IsSet);

                Assert.NotNull(secondGet.Item);
                Assert.Equal("test data", secondGet.Item?.TestData);
            }
        }

        [Fact]
        public async void TestNestedGetForUseFailsWithTimeout()
        {
            // pretty sure this will fail and be pretty tough to work around.
            using (var firstGet = await store.GetForUse(1))
            {
                firstGet.Item = new TestItem("test data");

                await Assert.ThrowsAsync<TimeoutException>(() => store.GetForUse(1));
            }
        }

        public class TestItem
        {
            public readonly string TestData;

            public TestItem(string testData)
            {
                this.TestData = testData;
            }
        }
    }
}
