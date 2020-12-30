// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
