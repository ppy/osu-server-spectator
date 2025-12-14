using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp.Models;

/// <summary>
/// The Bradley-Terry Full model assumes a single scalar value to represent player performance, allows for rating
/// updates based on match outcomes, and uses a logistic regression approach to estimate player ratings. However,
/// it differs from the Thurstone-Muller model in terms of the estimation technique, which provides an alternative
/// perspective on player ratings.
/// </summary>
public class BradleyTerryFull : OpenSkillModelBase
{
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
                (double omega, double delta) = teamRatings
                    .Index()
                    .Where(q => q.Index != iTeamIndex)
                    .Aggregate((sumOmega: 0D, sumDelta: 0D), (acc, q) =>
                    {
                        (int qTeamIndex, ITeamRating qTeam) = q;

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

                        double ciq = Math.Sqrt(iTeam.SigmaSq + qTeam.SigmaSq + (2 * BetaSq));
                        double piq = 1 / (1 + Math.Exp((qTeam.Mu - iTeam.Mu) * marginFactor / ciq));
                        double sigmaToCiq = iTeam.SigmaSq / ciq;
                        double s = Common.Score(qTeam.Rank, iTeam.Rank);
                        double gamma = Gamma(
                            ciq,
                            teamRatings.Count,
                            iTeam.Mu,
                            iTeam.SigmaSq,
                            iTeam.Players,
                            iTeam.Rank,
                            weights?.ElementAt(iTeamIndex)
                        );

                        return (
                            sumOmega: acc.sumOmega + (sigmaToCiq * (s - piq)),
                            sumDelta: acc.sumDelta + (gamma * sigmaToCiq / ciq * piq * (1 - piq))
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