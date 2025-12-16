using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using OpenSkillSharp.Tests.TestingUtil;

namespace OpenSkillSharp.Tests.Models;

public class BradleyTerryPartTests
{
    private readonly ModelTestData _testData = ModelTestData.FromJson("bradleyterrypart");
    private BradleyTerryPart TestModel => new() { Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma };

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
        BradleyTerryPart marginTestModel = new()
        {
            Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, Margin = 2D
        };
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
        BradleyTerryPart limitSigmaTestModel = new()
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
        BradleyTerryPart balanceModel = new()
        {
            Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, Balance = true
        };
        IList<ITeam> teams = balanceModel.MockTeams(expectedRatings);

        // Act
        IEnumerable<ITeam> results = balanceModel.Rate(
            teams,
            [1, 2]
        );

        // Assert
        Assertions.RatingResultsEqual(expectedRatings, results);
    }

    [Fact]
    public void Rate_WindowSize0_Tau0()
    {
        // Arrange
        BradleyTerryPart windowTauModel = new()
        {
            Mu = _testData.Model.Mu, Sigma = _testData.Model.Sigma, Tau = 0, WindowSize = 0
        };
        IRating playerA = windowTauModel.Rating();
        IRating playerB = windowTauModel.Rating();

        // Act
        List<ITeam> results = windowTauModel.Rate(
            [
                new Team { Players = [playerA] },
                new Team { Players = [playerB] }
            ]
        ).ToList();

        // Assert
        Assertions.RatingsEqual(playerA, results.ElementAt(0).Players.ElementAt(0));
        Assertions.RatingsEqual(playerB, results.ElementAt(1).Players.ElementAt(0));
    }
}