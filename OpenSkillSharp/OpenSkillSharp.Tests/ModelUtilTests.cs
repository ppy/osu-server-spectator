using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests;

public class ModelUtilTests
{
    [Fact]
    public void ModelValues_Defaults()
    {
        PlackettLuce model = new();

        Assert.Equal(25D, model.Mu);
        Assert.Equal(25D / 3, model.Sigma);
        Assert.Equal(25D / 6, model.Beta);
        Assert.Equal(0.0001, model.Kappa);
        Assert.Equal(25D / 300, model.Tau);
        Assert.False(model.LimitSigma);
        Assert.False(model.Balance);
    }

    [Fact]
    public void CalculateTeamSqrtSigma()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] }
            ]
        ).ToList();

        double teamSqrtSigma = model.CalculateTeamSqrtSigma(teamRatings);

        Assert.Equal(15.590239, teamSqrtSigma, 0.000001);
    }

    [Fact]
    public void CalculateTeamSqrtSigma_5v5()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating(), model.Rating(), model.Rating(), model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating(), model.Rating(), model.Rating(), model.Rating()] }
            ]
        ).ToList();

        double teamSqrtSigma = model.CalculateTeamSqrtSigma(teamRatings);

        Assert.Equal(27.003, teamSqrtSigma, 0.001);
    }

    [Fact]
    public void CalculateSumQ()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] }
            ]
        ).ToList();
        double teamSqrtSigma = model.CalculateTeamSqrtSigma(teamRatings);

        IEnumerable<double> sumQ = model.CalculateSumQ(teamRatings, teamSqrtSigma);

        Assert.Equal([29.67892702634643, 24.70819334370875], sumQ);
    }

    [Fact]
    public void CalculateSumQ_5v5()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating(), model.Rating(), model.Rating(), model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating(), model.Rating(), model.Rating(), model.Rating()] }
            ]
        ).ToList();
        double teamSqrtSigma = model.CalculateTeamSqrtSigma(teamRatings);

        List<double> sumQ = model.CalculateSumQ(teamRatings, teamSqrtSigma).ToList();

        Assert.Equal(204.8437881, sumQ[0], 0.0001);
        Assert.Equal(102.421894, sumQ[1], 0.0001);
    }

    [Theory]
    [InlineData(2, 2, 3, 4, 0, 1)]
    [InlineData(2, 2, 3, 16, 0, 2)]
    [InlineData(2, 2, 3, 64, 1, 4)]
    public void CalculateGamma(
        double c,
        double k,
        double mu,
        double sigmaSq,
        double qRank,
        double expected
    )
    {
        PlackettLuce model = new();

        double gamma = model.Gamma(
            c,
            k,
            mu,
            sigmaSq,
            [model.Rating(), model.Rating(), model.Rating(), model.Rating(), model.Rating()],
            qRank,
            null
        );

        Assert.Equal(expected, gamma);
    }
}