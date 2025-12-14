using System.Diagnostics.CodeAnalysis;

using OpenSkillSharp.Util;

namespace OpenSkillSharp.Tests.Util;

public class StatisticsTests
{
    private const double Tolerance = 0.00001;

    [Theory]
    [InlineData(1, 2, 1.5251352044082924)]
    [InlineData(0, 2, 2.3732157475120528)]
    [InlineData(0, -1, 0.2875999734906994)]
    [InlineData(0, 10, 10)]
    public void V(double x, double t, double expected)
    {
        Assert.Equal(expected, Statistics.V(x, t), Tolerance);
    }

    [Theory]
    [InlineData(1, 2, 0.8009021873172315)]
    [InlineData(0, 2, 0.8857214892150859)]
    [InlineData(0, -1, 0.3703137182425503)]
    [InlineData(0, 10, 0)]
    [InlineData(-1, 10, 1)]
    public void W(double x, double t, double expected)
    {
        Assert.Equal(expected, Statistics.W(x, t), Tolerance);
    }

    [Theory]
    [InlineData(-1000, -100, 1100)]
    [InlineData(1000, -100, -1100)]
    [InlineData(-1000, 1000, 0.79788)]
    [InlineData(0, 1000, 0)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public void VT(double x, double t, double expected)
    {
        Assert.Equal(expected, Statistics.VT(x, t), Tolerance);
    }

    [Theory]
    [InlineData(1, 2, 0.38385826878672835)]
    [InlineData(0, 2, 0.22625869547437663)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 10, 0)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public void WT(double x, double t, double expected)
    {
        Assert.Equal(expected, Statistics.WT(x, t), Tolerance);
    }
}