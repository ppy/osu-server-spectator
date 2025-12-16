using OpenSkillSharp.Util;

namespace OpenSkillSharp.Tests.Util;

public class EnumerableExtensionTests
{
    public static IEnumerable<object[]> NormalizeTestData => new List<object[]>
    {
        new object[] { new List<double> { 1, 2, 3 }, 0, 1, new List<double> { 0, 0.5, 1 } },
        new object[] { new List<double> { 1, 2, 3 }, 0, 100, new List<double> { 0, 50, 100 } },
        new object[] { new List<double> { 1, 2, 3 }, 0, 10, new List<double> { 0, 5, 10 } },
        new object[] { new List<double> { 1, 2, 3 }, 1, 0, new List<double> { 1, 0.5, 0 } },
        new object[] { new List<double> { 1, 1, 1 }, 0, 1, new List<double> { 0, 0, 0 } }
    };

    public static IEnumerable<object[]> UnwindTestData => new List<object[]>
    {
        // Zero items
        new object[] { new List<string>(), new List<double>(), new List<string>(), new List<double>() },
        // Accepts 1 item
        new object[]
        {
            new List<string> { "a" }, new List<double> { 0 }, new List<string> { "a" }, new List<double> { 0 }
        },
        // Accepts 2 items
        new object[]
        {
            new List<string> { "b", "a" }, new List<double> { 1, 0 }, new List<string> { "a", "b" },
            new List<double> { 1, 0 }
        },
        // Accepts 3 items
        new object[]
        {
            new List<string> { "b", "c", "a" }, new List<double> { 1, 2, 0 }, new List<string> { "a", "b", "c" },
            new List<double> { 2, 0, 1 }
        },
        // Accepts 4 items
        new object[]
        {
            new List<string> { "b", "d", "c", "a" }, new List<double> { 1, 3, 2, 0 },
            new List<string> { "a", "b", "c", "d" }, new List<double> { 3, 0, 2, 1 }
        }
    };

    [Theory]
    [MemberData(nameof(NormalizeTestData))]
    public void Normalize(
        IList<double> source,
        double min,
        double max,
        IList<double> expected
    )
    {
        Assert.Equal(source.Normalize(min, max), expected);
    }

    [Theory]
    [MemberData(nameof(UnwindTestData))]
    public void Unwind(
        IList<string> source,
        IList<double> rank,
        IList<string> expectedOutput,
        IList<double> expectedTenet
    )
    {
        (IList<string>? output, IList<double>? tenet) = rank.Unwind(source);

        Assert.Equal(expectedOutput, output);
        Assert.Equal(expectedTenet, tenet);
    }

    [Fact]
    public void Unwind_AllowsNonIntegerRanks()
    {
        List<string> expectedOutput = new()
        {
            "d",
            "a",
            "c",
            "b",
            "f",
            "e"
        };
        List<string> source = new()
        {
            "a",
            "b",
            "c",
            "d",
            "e",
            "f"
        };
        List<double> rank = new()
        {
            0.28591,
            0.42682,
            0.35912,
            0.21237,
            0.60619,
            0.47078
        };

        (IList<string> output, _) = rank.Unwind(source);

        Assert.Equal(expectedOutput, output);
    }

    [Fact]
    public void Unwind_CanUndoRanking()
    {
        List<double> rank = Enumerable.Range(0, 100).Select(i => (double)i).ToList();
        int[] source = rank.Select(r => Random.Shared.Next()).ToArray();
        Random.Shared.Shuffle(source);

        (IList<int>? trans, IList<double>? tenet) = rank.Unwind(source);
        (IList<int>? output, IList<double>? outputTenet) = tenet.Unwind(trans);

        Assert.Equal(source, output);
        Assert.Equal(rank, outputTenet);
    }
}