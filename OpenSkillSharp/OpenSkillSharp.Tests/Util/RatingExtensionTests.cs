using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp.Tests.Util;

public class RatingExtensionTests
{
    [Fact]
    public void CalculateRankings_GivenPartialScores_FallsBackToTeamIndex()
    {
        PlackettLuce model = new();
        List<ITeam> teams =
        [
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] }
        ];
        List<double> scores = [1, 2, 3];

        List<double> ranks = teams.CalculateRankings(scores.Select(s => -s).ToList()).ToList();

        Assert.Equal([2, 1, 0, 3], ranks);
    }
    
    [Fact]
    public void CalculateRankings_GivenInverseScores_ProperlyConvertsToRanks()
    {
        PlackettLuce model = new();
        List<ITeam> teams =
        [
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] }
        ];
        List<double> scores = [3, 0, 2, 1];

        List<double> ranks = teams.CalculateRankings(scores.Select(s => -s).ToList()).ToList();

        Assert.Equal([0, 3, 1, 2], ranks);
    }

    [Fact]
    public void CalculateRankings_GivenNoRanks_DefaultsToTeamIndex()
    {
        PlackettLuce model = new();
        List<ITeam> teams =
        [
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] },
            new Team { Players = [model.Rating()] }
        ];

        List<double> ranks = teams.CalculateRankings().ToList();

        Assert.Equal([0, 1, 2, 3], ranks);
    }

    [Fact]
    public void CalculateRankings_GivenNoTeams_ProducesEmptyList()
    {
        List<ITeam> teams = [];

        List<double> ranks = teams.CalculateRankings().ToList();

        Assert.Equal([], ranks);
    }
    
    [Fact]
    public void CountRankOccurrences()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] }
            ]
        ).ToList();

        List<int> rankOccurrences = teamRatings.CountRankOccurrences().ToList();

        Assert.Equal([1, 1], rankOccurrences);
    }

    [Fact]
    public void CountRankOccurrences_1TeamPerRank()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating()] }
            ]
        ).ToList();

        List<int> rankOccurrences = teamRatings.CountRankOccurrences().ToList();

        Assert.Equal([1, 1, 1, 1], rankOccurrences);
    }

    [Fact]
    public void CountRankOccurrences_SharedRanks()
    {
        PlackettLuce model = new();
        List<ITeamRating> teamRatings = model.CalculateTeamRatings(
            [
                new Team { Players = [model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating(), model.Rating()] },
                new Team { Players = [model.Rating()] }
            ],
            [1, 1, 1, 4]
        ).ToList();

        List<int> rankOccurrences = teamRatings.CountRankOccurrences().ToList();

        Assert.Equal([3, 3, 3, 1], rankOccurrences);
    }
}