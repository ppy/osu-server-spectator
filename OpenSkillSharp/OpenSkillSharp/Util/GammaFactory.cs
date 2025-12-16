using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Util;

/// <summary>
/// Represents a function to calculate a gamma value.
/// </summary>
/// <param name="c">The square root of the collective team sigma.</param>
/// <param name="k">The number of teams in the game.</param>
/// <param name="mu">The mean of the team's rating.</param>
/// <param name="sigmaSq">The variance of the team's rating.</param>
/// <param name="team">A list of rating objects representing the team.</param>
/// <param name="qRank">The rank of the team.</param>
/// <param name="weights">The weights of the players on the team.</param>
public delegate double GammaFactory(
    double c,
    double k,
    double mu,
    double sigmaSq,
    IEnumerable<IRating> team,
    double qRank,
    IEnumerable<double>? weights
);