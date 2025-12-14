using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp.Models;

/// <summary>
/// The Plackett-Luce model departs from singular scalar representations of player performance in simpler models.
/// There is a vector of abilities for each player that captures their performance across multiple dimensions.
/// The outcome of a match between multiple players depends on their abilities in each dimension. By introducing
/// this multidimensional aspect, the Plackett-Luce model provides a richer framework for ranking players
/// based on their abilities in various dimensions.
/// </summary>
public class PlackettLuce : OpenSkillModelBase
{
    protected override IEnumerable<ITeam> Compute(
        IList<ITeam> teams,
        IList<double>? ranks = null,
        IList<double>? scores = null,
        IList<IList<double>>? weights = null
    )
    {
        List<ITeamRating> teamRatings = CalculateTeamRatings(teams, ranks).ToList();
        double c = CalculateTeamSqrtSigma(teamRatings);
        List<double> sumQ = CalculateSumQ(teamRatings, c, ranks).ToList();
        List<int> rankOccurrences = teamRatings.CountRankOccurrences().ToList();
        List<double> adjustedMus = CalculateMarginAdjustedMu(teamRatings, scores).ToList();

        List<Team> result = teamRatings.Select((iTeam, iTeamIndex) =>
        {
            // Calculate omega and delta
            double iMuOverC = Math.Exp(adjustedMus[iTeamIndex] / c);
            (double omega, double delta) = teamRatings
                .Select((qTeam, qTeamIndex) => (qTeam, qTeamIndex))
                .Where(x => x.qTeam.Rank <= iTeam.Rank)
                .Aggregate((sumOmega: 0D, sumDelta: 0D), (acc, q) =>
                {
                    (ITeamRating _, int qTeamIndex) = q;
                    double iMuOverCeOverSumQ = iMuOverC / sumQ[qTeamIndex];

                    return (
                        sumOmega: acc.sumOmega + (
                            iTeamIndex == qTeamIndex
                                ? 1 - (iMuOverCeOverSumQ / rankOccurrences[qTeamIndex])
                                : -1 * iMuOverCeOverSumQ / rankOccurrences[qTeamIndex]
                        ),
                        sumDelta: acc.sumDelta +
                                  (iMuOverCeOverSumQ * (1 - iMuOverCeOverSumQ) / rankOccurrences[qTeamIndex])
                    );
                });

            omega *= iTeam.SigmaSq / c;
            delta *= iTeam.SigmaSq / Math.Pow(c, 2);
            delta *= Gamma(
                c,
                teamRatings.Count,
                iTeam.Mu,
                iTeam.SigmaSq,
                iTeam.Players,
                iTeam.Rank,
                weights?.ElementAtOrDefault(iTeamIndex)
            );

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

        AdjustPlayerMuChangeForTie(teams, teamRatings, result);

        return result;
    }
}