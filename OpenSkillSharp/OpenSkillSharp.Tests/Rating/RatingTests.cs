using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using OpenSkillSharp.Tests.TestingUtil;

namespace OpenSkillSharp.Tests.Rating;

public class RatingTests
{
    [Fact]
    public void Values_DefaultConstructor_Defaults()
    {
        OpenSkillSharp.Rating.Rating rating = new();

        Assert.Equal(25D, rating.Mu);
        Assert.Equal(25D / 3, rating.Sigma);
    }

    [Fact]
    public void Values_FromModel_Defaults()
    {
        IRating rating = new PlackettLuce().Rating();

        Assert.Equal(25D, rating.Mu);
        Assert.Equal(25D / 3, rating.Sigma);
    }

    [Fact]
    public void Values_ReflectModelOverrides()
    {
        const double overrideMu = 30D;
        const double overrideSigma = 30D / 3;

        PlackettLuce model = new() { Mu = overrideMu, Sigma = overrideSigma };
        IRating rating = model.Rating();

        Assert.Equal(overrideMu, rating.Mu);
        Assert.Equal(overrideSigma, rating.Sigma);
    }

    [Fact]
    public void Values_CanBeOverriddenFromModel()
    {
        const double overrideMu = 30D;
        const double overrideSigma = 30D / 3;

        PlackettLuce model = new();
        IRating rating = model.Rating(overrideMu, overrideSigma);

        Assert.Equal(overrideMu, rating.Mu);
        Assert.Equal(overrideSigma, rating.Sigma);
        // Passing values to IOpenSkillModel.Rating() should be a pure operation and not modify the model
        Assert.Equal(25D, model.Mu);
        Assert.Equal(25D / 3, model.Sigma);
    }

    [Fact]
    public void Ordinal_UsingGetter_ReturnsCorrectValue()
    {
        OpenSkillSharp.Rating.Rating rating = new() { Mu = 5, Sigma = 2 };

        Assert.Equal(-1D, rating.Ordinal);
    }

    [Fact]
    public void GetOrdinal_GivenAlphaAndTarget_ReturnsCorrectValue()
    {
        OpenSkillSharp.Rating.Rating rating = new() { Mu = 24, Sigma = 6 };

        double result = rating.GetOrdinal(alpha: 24, target: 1500);

        Assert.Equal(1644D, result);
    }

    [Fact]
    public void GetOrdinal_GivenZ_ReturnsCorrectValue()
    {
        OpenSkillSharp.Rating.Rating rating = new() { Mu = 24, Sigma = 6 };

        double result = rating.GetOrdinal(2);

        Assert.Equal(12D, result);
    }

    [Fact]
    public void Clone_CreatesNewInstance()
    {
        OpenSkillSharp.Rating.Rating rating = new() { Mu = 25, Sigma = 25D / 3 };

        IRating clone = rating.Clone();

        Assertions.RatingsEqual(rating, clone);
        Assert.NotSame(rating, clone);
    }
}