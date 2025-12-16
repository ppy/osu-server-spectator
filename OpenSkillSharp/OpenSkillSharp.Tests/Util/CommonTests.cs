using OpenSkillSharp.Util;

namespace OpenSkillSharp.Tests.Util;

public class CommonTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0.5)]
    public void Score(double q, double i, double expected)
    {
        Assert.Equal(expected, Common.Score(q, i));
    }
}