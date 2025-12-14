using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Util;

public static class RatingExtensions
{
    /// <summary>
    /// Calculates rankings based on the scores of the given teams.
    /// </summary>
    /// <param name="game">A list of teams in a game.</param>
    /// <param name="ranks">
    /// An optional list of numbers corresponding to the given teams where higher values represent winners.
    /// </param>
    /// <returns>A list of numbers representing ranks for each given team.</returns>
    public static IEnumerable<double> CalculateRankings(
        this IList<ITeam> game,
        IList<double>? ranks = null
    )
    {
        if (!game.Any())
        {
            return new List<double>();
        }

        List<double> teamScores = game.Select((_, idx) => ranks?.ElementAtOrDefault(idx) ?? idx).ToList();

        Dictionary<double, double> rankMap = teamScores
            .OrderBy(s => s)
            .Select((score, idx) => (score, idx))
            .GroupBy(t => t.score)
            .ToDictionary(g => g.Key, g => (double)g.First().idx);

        return teamScores.Select(s => rankMap[s]).ToList();
    }

    /// <summary>
    /// Calculates the number of times a team's rank occurs in a game.
    /// </summary>
    /// <param name="teamRatings">A list of team ratings in a game.</param>
    /// <returns>
    /// A list of numbers representing the number of times a team's rank occured in the game, one for each given team.
    /// </returns>
    public static IEnumerable<int> CountRankOccurrences(this IList<ITeamRating> teamRatings)
    {
        Dictionary<int, int> rankCounts = teamRatings
            .GroupBy(tr => tr.Rank)
            .ToDictionary(g => g.Key, g => g.Count());

        return teamRatings.Select(team => rankCounts[team.Rank]);
    }
}