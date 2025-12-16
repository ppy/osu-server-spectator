using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp.Models;

/// <summary>
/// The Thurstone-Mosteller with Partial Pairing model extends the full pairing model to handle scenarios where not
/// all players compete against each other. It retains the assumptions of the full pairing model - utilizing a single
/// scalar value to represent player performance, enabling rating updates through match outcomes, and employing
/// maximum likelihood estimation for rating estimation. This model relaxes the requirement for complete pairing and
/// is ideal for situations where only specific players directly compete with each other.
/// </summary>
public class ThurstoneMostellerPart : OpenSkillModelBase
{
    /// <summary>
    /// The sliding window size for partial pairing such that a larger window size tends to full pairing mode accuracy.
    /// </summary>
    public int WindowSize { get; set; } = 4;

    /// <summary>
    /// Represents the draw margin for Thurstone-Mosteller models.
    /// </summary>
    public double Epsilon { get; set; } = 0.1;

    protected override IEnumerable<ITeam> Compute(
        IList<ITeam> teams,
        IList<double>? ranks = null,
        IList<double>? scores = null,
        IList<IList<double>>? weights = null
    )
    {
        List<ITeamRating> teamRatings = CalculateTeamRatings(teams, ranks).ToList();

        return teamRatings.Select((iTeam, iTeamIndex) =>
        {
            (double omega, double delta, int nComparisons) = teamRatings
                .Index()
                .Skip(Math.Max(0, iTeamIndex - WindowSize))
                .Take(Math.Min(teamRatings.Count, iTeamIndex + WindowSize + 1))
                .Where(q => q.Index != iTeamIndex)
                .Aggregate((sumOmega: 0D, sumDelta: 0D, nComparisons: 0), (acc, q) =>
                {
                    (int qTeamIndex, ITeamRating qTeam) = q;

                    double ciq = 2 * Math.Sqrt(iTeam.SigmaSq + qTeam.SigmaSq + (2 * BetaSq));
                    double deltaMu = (iTeam.Mu - qTeam.Mu) / ciq;
                    double sigmaToCiq = iTeam.SigmaSq / ciq;
                    double gamma = Gamma(
                        ciq,
                        teamRatings.Count,
                        iTeam.Mu,
                        iTeam.SigmaSq,
                        iTeam.Players,
                        iTeam.Rank,
                        weights?.ElementAt(iTeamIndex)
                    );

                    // Margin factor adjustment
                    double marginFactor = 1;
                    if (scores is not null)
                    {
                        double scoreDiff = Math.Abs(scores[qTeamIndex] - scores[iTeamIndex]);
                        if (scoreDiff > 0 && Margin > 0 && scoreDiff > Margin && qTeam.Rank < iTeam.Rank)
                        {
                            marginFactor = Math.Log(1 + (scoreDiff / Margin));
                        }
                    }

                    if (iTeam.Rank == qTeam.Rank)
                    {
                        return (
                            sumOmega: acc.sumOmega + (
                                sigmaToCiq * Statistics.VT(deltaMu * marginFactor, Epsilon / ciq)
                            ),
                            sumDelta: acc.sumDelta + (
                                gamma * sigmaToCiq / ciq * Statistics.WT(deltaMu * marginFactor, Epsilon / ciq)
                            ),
                            nComparisons: acc.nComparisons + 1
                        );
                    }

                    int sign = qTeam.Rank > iTeam.Rank ? 1 : -1;
                    return (
                        sumOmega: acc.sumOmega + (
                            sign * sigmaToCiq * Statistics.V(sign * deltaMu * marginFactor, Epsilon / ciq)
                        ),
                        sumDelta: acc.sumDelta + (
                            gamma * sigmaToCiq / ciq * Statistics.W(sign * deltaMu * marginFactor, Epsilon / ciq)
                        ),
                        nComparisons: acc.nComparisons + 1
                    );
                });

            if (nComparisons > 0)
            {
                omega /= nComparisons;
                delta /= nComparisons;
            }

            return new Team
            {
                Players = UpdatePlayerRatings(
                    teams[iTeamIndex],
                    iTeam,
                    omega,
                    delta,
                    weights?.ElementAtOrDefault(iTeamIndex)
                )
            };
        }).ToList();
    }
}