using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp.Models;

/// <summary>
/// The Thurstone-Mosteller with Full Pairing model assumes a single scalar value to represent player performance
/// and enables rating updates based on match outcomes. Additionally, it employs a maximum likelihood estimation
/// approach to estimate the ratings of players as per the observed outcomes. These assumptions contribute to the
/// model's ability to estimate ratings accurately and provide a well-founded ranking of players.
/// </summary>
public class ThurstoneMostellerFull : OpenSkillModelBase
{
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

        List<Team> result = teamRatings
            .Select((iTeam, iTeamIndex) =>
            {
                // Calculate omega and delta
                (double omega, double delta) = teamRatings
                    .Index()
                    .Where(q => q.Index != iTeamIndex)
                    .Aggregate((sumOmega: 0D, sumDelta: 0D), (acc, q) =>
                    {
                        (int qTeamIndex, ITeamRating qTeam) = q;

                        double ciq = Math.Sqrt(iTeam.SigmaSq + qTeam.SigmaSq + (2 * BetaSq));
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
                                )
                            );
                        }

                        int sign = qTeam.Rank > iTeam.Rank ? 1 : -1;
                        return (
                            sumOmega: acc.sumOmega + (
                                sign * sigmaToCiq * Statistics.V(sign * deltaMu * marginFactor, Epsilon / ciq)
                            ),
                            sumDelta: acc.sumDelta + (
                                gamma * sigmaToCiq / ciq * Statistics.W(sign * deltaMu * marginFactor, Epsilon / ciq)
                            )
                        );
                    });

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