using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using OpenSkillSharp.Tests.TestingUtil;

namespace OpenSkillSharp.Tests.Models;

public class ThurstoneMostellerFullTests
{
    private readonly ModelTestData _testData = ModelTestData.FromJson("thurstonemostellerfull");
    private ThurstoneMostellerFull TestModel => new() { Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma };

    [Fact]
    public void Rate_Normal()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Normal;
        IList<ITeam> teams = TestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = TestModel.Rate(teams);

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Ranks()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Ranks;
        IList<ITeam> teams = TestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = TestModel.Rate(
            teams,
            [2, 1, 4, 3]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Scores()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Scores;
        IList<ITeam> teams = TestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = TestModel.Rate(
            teams,
            scores: [1, 2]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Margins()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Margins;
        ThurstoneMostellerFull marginTestModel =
            new() { Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, Margin = 2D };
        IList<ITeam> teams = marginTestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = marginTestModel.Rate(
            teams,
            scores: [10, 5, 5, 2, 1],
            weights: [[1, 2], [2, 1], [1, 2], [3, 1], [1, 2]]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_LimitSigma()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.LimitSigma;
        ThurstoneMostellerFull limitSigmaTestModel = new()
        {
            Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, LimitSigma = true
        };
        IList<ITeam> teams = limitSigmaTestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = limitSigmaTestModel.Rate(
            teams,
            [2, 1, 3]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Ties()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Ties;
        IList<ITeam> teams = TestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = TestModel.Rate(
            teams,
            [1, 2, 1]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Weights()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Weights;
        IList<ITeam> teams = TestModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = TestModel.Rate(
            teams,
            [2, 1, 4, 3],
            weights: [[2, 0, 0], [1, 2], [0, 0, 1], [0, 1]]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_Balance()
    {
        // Arrange
        IList<ITeam> expectedRatings = _testData.Balance;
        ThurstoneMostellerFull balanceModel =
            new() { Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, Balance = true };
        IList<ITeam> teams = balanceModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = balanceModel.Rate(
            teams,
            [1, 2]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }
}