// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;

namespace osu.Server.Spectator.Extensions
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Interleaves sequential elements from one or more collections, returning groups of interleaved elements.
        /// </summary>
        /// <remarks>
        /// Runtime complexity is <c>O(n * m)</c> where <c>n</c> is the given number of collections
        /// and <c>m</c> is the maximum number of elements in of any collection.
        /// </remarks>
        /// <param name="collections">The collections to interleave.</param>
        /// <typeparam name="T">The type of element in each collection.</typeparam>
        /// <returns>
        /// For the collections:
        /// <code>
        /// A = [A1, A2, A3, A4]
        /// B = [B1, B2]
        /// C = [C1, C2, C3]
        /// </code>
        /// This returns the groups of elements <c>[A1, B1, C1]</c>, <c>[A2, B2, C2]</c>, <c>[A3, C3]</c>, <c>[A4]</c>.
        /// </returns>
        public static IEnumerable<IEnumerable<T>> Interleave<T>(this IEnumerable<IEnumerable<T>> collections)
        {
            var enumerators = new List<IEnumerator<T>>();

            try
            {
                foreach (var c in collections)
                    enumerators.Add(c.GetEnumerator());

                while (true)
                {
                    T[] interleaved = enumerators.Where(it => it.MoveNext())
                                                 .Select(it => it.Current)
                                                 .ToArray(); // The enumerators must be consumed immediately due to lazy evaluation.

                    if (interleaved.Length == 0)
                        break;

                    yield return interleaved;
                }
            }
            finally
            {
                foreach (var it in enumerators)
                    it.Dispose();
            }
        }
    }
}
