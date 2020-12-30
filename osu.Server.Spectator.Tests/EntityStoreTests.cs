// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class EntityStoreTests
    {
        private readonly EntityStore<TestItem> store;

        public EntityStoreTests()
        {
            store = new EntityStore<TestItem>();
        }

        [Fact]
        public async void TestGetTwiceRetainsItem()
        {
            using (var firstGet = await store.GetForUse(1, true))
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
        public async void TestGetWithoutLockFails()
        {
            ItemUsage<TestItem>? retrieval;

            using (retrieval = await store.GetForUse(1, true))
                retrieval.Item = new TestItem("test data");

            Assert.Throws<InvalidOperationException>(() => retrieval.Item);
        }

        [Fact]
        public async void TestSetWithoutLockFails()
        {
            ItemUsage<TestItem>? retrieval;

            using (retrieval = await store.GetForUse(1, true))
            {
            }

            Assert.Throws<InvalidOperationException>(() => retrieval.Item = new TestItem("test data"));
        }

        [Fact]
        public async void TestDestroyingTrackedEntity()
        {
            using (var firstGet = await store.GetForUse(1, true))
            {
                firstGet.Item = new TestItem("test data");
            }

            using (var secondGet = await store.GetForUse(1))
                Assert.NotNull(secondGet.Item);

            await store.Destroy(1);

            await Assert.ThrowsAsync<ArgumentException>(() => store.GetForUse(1));

            using (var thirdGet = await store.GetForUse(1, true))
                Assert.Null(thirdGet.Item);
        }

        [Fact]
        public async void TestDestroyingFromInsideUsage()
        {
            using (var firstGet = await store.GetForUse(1, true))
            {
                firstGet.Item = new TestItem("test data");
            }

            using (var secondGet = await store.GetForUse(1))
            {
                Assert.NotNull(secondGet.Item);
                secondGet.Destroy();
                Assert.Throws<InvalidOperationException>(() => secondGet.Item);
            }

            await Assert.ThrowsAsync<ArgumentException>(() => store.GetForUse(1));

            using (var thirdGet = await store.GetForUse(1, true))
                Assert.Null(thirdGet.Item);
        }

        [Fact]
        public async void TestDestroyingWithoutLockFails()
        {
            using (var firstGet = await store.GetForUse(1, true))
                firstGet.Item = new TestItem("test data");

            ItemUsage<TestItem>? secondGet;

            using (secondGet = await store.GetForUse(1))
                Assert.NotNull(secondGet.Item);

            Assert.Throws<InvalidOperationException>(() => secondGet.Destroy());
        }

        [Fact]
        public async void TestGetTwiceWithDelayedReturn()
        {
            var firstLockAchieved = new ManualResetEventSlim();
            var firstLockDelayComplete = new ManualResetEventSlim();

            new Thread(async () =>
            {
                using (var firstGet = await store.GetForUse(1, true))
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
            using (var firstGet = await store.GetForUse(1, true))
            {
                firstGet.Item = new TestItem("test data");

                await Assert.ThrowsAsync<TimeoutException>(() => store.GetForUse(1));
            }
        }

        [Fact]
        public async Task TestGetAllEntitiesReadsConsistentState()
        {
            using (var firstGet = await store.GetForUse(1, true))
                firstGet.Item = new TestItem("a");

            using (var secondGet = await store.GetForUse(2, true))
                secondGet.Item = new TestItem("b");

            using (await store.GetForUse(3, true))
            {
                // keep this item null.
                // we'll be testing that this isn't returned later.
            }

            KeyValuePair<long, TestItem>[] items = new KeyValuePair<long, TestItem>[0];

            ManualResetEventSlim backgroundRetrievalStarted = new ManualResetEventSlim();
            ManualResetEventSlim backgroundRetrievalDone = new ManualResetEventSlim();

            Thread backgroundRetrievalThread = new Thread(() =>
            {
                backgroundRetrievalStarted.Set();
                items = store.GetAllEntities();
                backgroundRetrievalDone.Set();
            });

            using (var fourthGet = await store.GetForUse(4, true))
            {
                // start background retrieval while this get is holding the lock.
                backgroundRetrievalThread.Start();
                backgroundRetrievalStarted.Wait(1000);
                fourthGet.Item = new TestItem("c");
            }

            Assert.True(backgroundRetrievalDone.Wait(1000));

            Assert.NotNull(items);
            Assert.Equal(3, items.Length);
            Assert.DoesNotContain(3, items?.Select(item => item.Key));
            Assert.All(items, item => Assert.NotNull(item.Value));
        }

        [Fact]
        public async Task TestClearOperationIsSerialised()
        {
            using (var firstGet = await store.GetForUse(1, true))
                firstGet.Item = new TestItem("hello");

            using (var secondGet = await store.GetForUse(2, true))
                secondGet.Item = new TestItem("there");

            ManualResetEventSlim clearOperationStarted = new ManualResetEventSlim();
            ManualResetEventSlim clearOperationDone = new ManualResetEventSlim();
            Thread backgroundClearThread = new Thread(() =>
            {
                clearOperationStarted.Set();
                store.Clear();
                clearOperationDone.Set();
            });

            using (var thirdGet = await store.GetForUse(3, true))
            {
                // start background clear while this get is holding the lock.
                backgroundClearThread.Start();
                clearOperationStarted.Wait(1000);
                thirdGet.Item = new TestItem("another");
            }

            Assert.True(clearOperationDone.Wait(1000));
            Assert.Empty(store.GetAllEntities());
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
