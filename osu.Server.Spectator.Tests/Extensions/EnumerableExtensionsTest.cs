// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Server.Spectator.Extensions;
using Xunit;

namespace osu.Server.Spectator.Tests.Extensions
{
    public class EnumerableExtensionsTest
    {
        [Fact]
        public void InterleaveNoCollectionsDoesNotReturnAny()
        {
            IEnumerable<IEnumerable<int>> collections = Array.Empty<IEnumerable<int>>();
            Assert.Empty(collections.Interleave());
        }

        [Fact]
        public void InterleaveEmptyCollectionsDoesNotReturnAny()
        {
            IEnumerable<IEnumerable<int>> collections = new[] { Array.Empty<int>(), Array.Empty<int>() };
            Assert.Empty(collections.Interleave());
        }

        [Fact]
        public void InterleaveSingleCollectionReturnsAllElementsFromCollection()
        {
            const int count = 10;

            IEnumerable<IEnumerable<int>> collections = new[] { Enumerable.Range(0, count) };

            IEnumerable<int>[] result = collections.Interleave().ToArray();
            Assert.Equal(count, result.Length);

            for (int i = 0; i < count; i++)
                Assert.Equal(new[] { i }, result[i]);
        }

        [Fact]
        public void InterleaveCollectionsOfSameCountReturnsInterleavedElements()
        {
            const int count = 10;

            IEnumerable<IEnumerable<int>> collections = new[] { Enumerable.Range(0, count), Enumerable.Range(0, count).Reverse() };

            IEnumerable<int>[] result = collections.Interleave().ToArray();
            Assert.Equal(count, result.Length);

            for (int i = 0; i < count; i++)
                Assert.Equal(new[] { i, count - i - 1 }, result[i]);
        }

        [Fact]
        public void InterleaveCollectionsOfDifferentLengthContinuesToCompletion()
        {
            const int max_count = 10;
            const int second_count = 5;
            const int third_count = 2;

            IEnumerable<IEnumerable<int>> collections = new[] { Enumerable.Range(0, max_count), Enumerable.Range(0, second_count), Enumerable.Range(0, third_count) };

            IEnumerable<int>[] result = collections.Interleave().ToArray();
            Assert.Equal(max_count, result.Length);

            for (int i = 0; i < max_count; i++)
            {
                List<int> expected = new List<int> { i };

                if (i < second_count)
                    expected.Add(i);

                if (i < third_count)
                    expected.Add(i);

                Assert.Equal(expected, result[i]);
            }
        }
    }
}
