using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp;

/// <summary>
/// Base class for all OpenSkill model implementations.
/// </summary>
public abstract class OpenSkillModelBase : IOpenSkillModel
{
    public double Mu { get; set; } = 25D;

    public double Sigma { get; set; } = 25D / 3;

    public double Beta { get; set; } = 25D / 6;

    public double BetaSq => Math.Pow(Beta, 2);

    public double Kappa { get; set; } = 0.0001;

    public GammaFactory Gamma { get; set; } = DefaultGamma;

    public double Tau { get; set; } = 25D / 300;

    public double Margin { get; set; }

    public bool LimitSigma { get; set; }

    public bool Balance { get; set; }

    public IRating Rating(double? mu = null, double? sigma = null)
    {
        return new Rating.Rating { Mu = mu ?? Mu, Sigma = sigma ?? Sigma };
    }

    public IEnumerable<ITeam> Rate(
        IList<ITeam> teams,
        IList<double>? ranks = null,
        IList<double>? scores = null,
        IList<IList<double>>? weights = null,
        double? tau = null
    )
    {
        if (ranks is not null)
        {
            if (!ranks.IsEqualLengthTo(teams))
            {
                throw new ArgumentException(
                    $"Arguments '{nameof(ranks)}' and '{nameof(teams)}' must be of equal length.");
            }

            if (scores is not null)
            {
                throw new ArgumentException(
                    $"Cannot except both '{nameof(ranks)}' and '{nameof(scores)}' at the same time."
                );
            }
        }

        if (scores is not null && !scores.IsEqualLengthTo(teams))
        {
            throw new ArgumentException($"Arguments '{nameof(scores)}' and '{nameof(teams)}' must be of equal length.");
        }

        if (weights is not null)
        {
            if (!weights.IsEqualLengthTo(teams))
            {
                throw new ArgumentException(
                    $"Arguments '{nameof(weights)}' and '{nameof(teams)}' must be of equal length.");
            }

            foreach ((int index, IList<double> weight) in weights.Index())
            {
                if (!weight.IsEqualLengthTo(teams[index].Players))
                {
                    throw new ArgumentException(
                        $"Size of team weights at index {index} does not match the size of the team.");
                }
            }
        }

        // Create a deep copy of the given teams
        IList<ITeam> originalTeams = teams;
        teams = originalTeams.Select(t => t.Clone()).ToList();

        // Correct sigma
        tau ??= Tau;
        double tauSq = Math.Pow(tau.Value, 2);
        foreach (IRating player in teams.SelectMany(t => t.Players))
        {
            player.Sigma = Math.Sqrt((player.Sigma * player.Sigma) + tauSq);
        }

        // Convert score to ranks
        if (ranks is null && scores is not null)
        {
            ranks = teams.CalculateRankings(scores.Select(s => -s).ToList()).ToList();
        }

        // Normalize weights
        weights = weights?.Select(w => w.Normalize(1, 2)).ToList();

        IList<double>? tenet = null;
        if (ranks is not null)
        {
            (IList<ITeam> orderedTeams, IList<double> orderedRanks) = ranks.Unwind(teams);

            if (weights is not null)
            {
                (weights, _) = ranks.Unwind(weights);
            }

            tenet = orderedRanks;
            teams = orderedTeams;
            ranks = ranks.OrderBy(r => r).ToList();
        }

        IList<ITeam> finalResult;
        if (ranks is not null && tenet is not null)
        {
            (finalResult, _) = tenet.Unwind(Compute(teams, ranks, scores, weights).ToList());
        }
        else
        {
            finalResult = Compute(teams, weights: weights).ToList();
        }

        if (LimitSigma)
        {
            foreach ((int teamIdx, ITeam team) in finalResult.Index())
            {
                foreach ((int playerIdx, IRating player) in team.Players.Index())
                {
                    player.Sigma = Math.Min(
                        player.Sigma,
                        originalTeams.ElementAt(teamIdx).Players.ElementAt(playerIdx).Sigma
                    );
                }
            }
        }

        return finalResult;
    }

    public IEnumerable<double> PredictWin(IList<ITeam> teams)
    {
        List<ITeamRating> teamRatings = CalculateTeamRatings(teams).ToList();
        int n = teams.Count;
        int denominator = n * (n - 1) / 2;

        return teamRatings.Select((teamA, idx) => teamRatings
                .Where((_, idy) => idx != idy)
                .Sum(teamB => Statistics.PhiMajor(
                    (teamA.Mu - teamB.Mu) / Math.Sqrt((n * BetaSq) + teamA.SigmaSq + teamB.SigmaSq)
                )) / denominator
        );
    }

    public double PredictDraw(IList<ITeam> teams)
    {
        List<ITeamRating> teamRatings = CalculateTeamRatings(teams).ToList();

        int playerCount = teamRatings.SelectMany(t => t.Players).Count();
        double drawProbability = 1D / playerCount;
        double drawMargin = Math.Sqrt(playerCount) * Beta * Statistics.InversePhiMajor((1 + drawProbability) / 2D);

        return teamRatings.SelectMany((teamA, i) =>
            teamRatings
                .Skip(i + 1)
                .Select(teamB =>
                {
                    double denominator = Math.Sqrt((playerCount * BetaSq) + teamA.SigmaSq + teamB.SigmaSq);
                    return Statistics.PhiMajor((drawMargin - teamA.Mu + teamB.Mu) / denominator)
                           - Statistics.PhiMajor((teamB.Mu - teamA.Mu - drawMargin) / denominator);
                })
        ).Average();
    }

    /// <summary>
    /// Creates team ratings for a game.
    /// </summary>
    /// <param name="teams">A list of teams in a game.</param>
    /// <param name="ranks">
    /// An optional list of numbers representing a rank for each team of <paramref name="teams"/>.
    /// </param>
    /// <returns>A list of team ratings.</returns>
    public IEnumerable<ITeamRating> CalculateTeamRatings(
        IList<ITeam> teams,
        IList<double>? ranks = null
    )
    {
        ranks ??= teams.CalculateRankings().ToList();

        return teams.Select((team, index) =>
        {
            double maxOrdinal = team.Players.Max(p => p.Ordinal);
            (double sumMu, double sumSigmaSq) = team.Players
                .Aggregate((mu: 0D, sigmaSq: 0D), (acc, player) =>
                {
                    double balanceWeight = Balance
                        ? 1 + ((maxOrdinal - player.Ordinal) / (maxOrdinal + Kappa))
                        : 1D;

                    return (
                        mu: acc.mu + (player.Mu * balanceWeight),
                        sigmaSq: acc.sigmaSq + Math.Pow(player.Sigma * balanceWeight, 2)
                    );
                });

            return new TeamRating
            {
                Players = team.Players, Mu = sumMu, SigmaSq = sumSigmaSq, Rank = (int)ranks[index]
            };
        });
    }

    /// <summary>
    /// Calculate the square root of the collective team sigma.
    /// </summary>
    /// <param name="teamRatings">A list of team ratings in a game.</param>
    /// <returns>A number representing the square root of the collective team sigma.</returns>
    public double CalculateTeamSqrtSigma(IList<ITeamRating> teamRatings)
    {
        return Math.Sqrt(teamRatings.Select(t => t.SigmaSq + BetaSq).Sum());
    }

    /// <summary>
    /// Sum up all values of (mu / c)^e
    /// </summary>
    /// <param name="teamRatings">A list of team ratings in a game.</param>
    /// <param name="c">The square root of the collective team sigma.</param>
    /// <param name="scores">
    /// An optional list of numbers representing a score for each team of <paramref name="teamRatings"/> used
    /// in margin factor calculation.
    /// </param>
    /// <returns>A list of numbers representing the SumQ for each team.</returns>
    public IEnumerable<double> CalculateSumQ(
        IList<ITeamRating> teamRatings,
        double c,
        IList<double>? scores = null
    )
    {
        // Calculate margin adjustment for team mu values if ranks are provided
        List<double> adjustedMus = CalculateMarginAdjustedMu(teamRatings, scores).ToList();

        return teamRatings.Select(qTeam => teamRatings
            .Select((iTeam, iTeamIndex) => (iTeam, iTeamIndex))
            .Where(x => x.iTeam.Rank >= qTeam.Rank)
            .Select(x => Math.Exp(adjustedMus[x.iTeamIndex] / c)).Sum()
        );
    }

    /// <summary>
    /// Calculate mu with margin adjustment for teams.
    /// </summary>
    /// <param name="teamRatings">A list of team ratings in a game.</param>
    /// <param name="scores">
    /// An optional list of numbers representing a score for each team of <paramref name="teamRatings"/> used
    /// in margin factor calculation.
    /// </param>
    /// <returns>A list of numbers representing the updated mu for each team.</returns>
    protected IEnumerable<double> CalculateMarginAdjustedMu(
        IList<ITeamRating> teamRatings,
        IList<double>? scores = null
    )
    {
        if (scores?.Count != teamRatings.Count)
        {
            return teamRatings.Select(t => t.Mu);
        }

        return teamRatings.Select((qTeam, qTeamIndex) =>
        {
            double qTeamScore = scores[qTeamIndex];
            double muAdjustment = teamRatings
                .Where((_, iTeamIndex) =>
                    qTeamIndex != iTeamIndex
                    && Math.Abs(qTeamScore - scores[iTeamIndex]) > 0
                )
                .Select((iTeam, iTeamIndex) =>
                {
                    double iTeamScore = scores[iTeamIndex];
                    double scoreDiff = Math.Abs(qTeamScore - iTeamScore);
                    double marginFactor = scoreDiff > Margin && Margin > 0
                        ? Math.Log(1 + (scoreDiff / Margin))
                        : 1D;

                    double sign = qTeamScore > iTeamScore ? 1D : -1D;
                    return (qTeam.Mu - iTeam.Mu) * (marginFactor - 1) * sign;
                })
                .Average();

            return qTeam.Mu + muAdjustment;
        });
    }

    /// <summary>
    /// Update player ratings in a game based on the calculated omega and delta.
    /// </summary>
    /// <param name="originalTeam">Original team.</param>
    /// <param name="team">Team rating.</param>
    /// <param name="omega">Omega.</param>
    /// <param name="delta">Delta.</param>
    /// <param name="weights">
    /// An optional list of numbers where each number represents the contribution of each player to the team's performance.
    /// </param>
    /// <returns>A list of updated rating objects.</returns>
    protected List<IRating> UpdatePlayerRatings(
        ITeam originalTeam,
        ITeamRating team,
        double omega,
        double delta,
        IEnumerable<double>? weights
    )
    {
        return team.Players.Select((_, jPlayerIndex) =>
        {
            IRating modifiedPlayer = originalTeam.Players.ElementAt(jPlayerIndex);
            double weight = weights?.ElementAtOrDefault(jPlayerIndex) ?? 1D;
            double weightScalar = omega >= 0
                ? weight
                : 1 / weight;

            modifiedPlayer.Mu += modifiedPlayer.Sigma * modifiedPlayer.Sigma / team.SigmaSq * omega * weightScalar;
            modifiedPlayer.Sigma *= Math.Sqrt(Math.Max(
                1 - (modifiedPlayer.Sigma * modifiedPlayer.Sigma / team.SigmaSq * delta * weightScalar),
                Kappa
            ));

            return modifiedPlayer;
        }).ToList();
    }

    /// <summary>
    /// Adjust player mu changes individually for teams in which a tie occurred.
    /// </summary>
    /// <param name="originalTeams">A list of teams in a game.</param>
    /// <param name="teamRatings">A list of team ratings in a game.</param>
    /// <param name="processedTeams">A list of teams in a game with updated ratings.</param>
    protected static void AdjustPlayerMuChangeForTie(
        IList<ITeam> originalTeams,
        IList<ITeamRating> teamRatings,
        IEnumerable<ITeam> processedTeams
    )
    {
        List<ITeam> processedTeamsList = processedTeams.ToList();
        Dictionary<int, List<int>> rankGroups = teamRatings
            .Select((tr, i) => new { tr.Rank, Index = i })
            .GroupBy(x => x.Rank)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Index).ToList());

        foreach (List<int> teamIndices in rankGroups.Values.Where(g => g.Count > 1))
        {
            double avgMuChange = teamIndices.Average(i =>
                processedTeamsList[i].Players.First().Mu - originalTeams[i].Players.First().Mu
            );

            foreach (int teamIndex in teamIndices)
            {
                foreach ((int playerIndex, IRating player) in processedTeamsList[teamIndex].Players.Index())
                {
                    player.Mu = originalTeams[teamIndex].Players.ElementAt(playerIndex).Mu + avgMuChange;
                }
            }
        }
    }

    /// <summary>
    /// The default gamma calculation for all models.
    /// </summary>
    protected static double DefaultGamma(
        double c,
        double k,
        double mu,
        double sigmaSq,
        IEnumerable<IRating> team,
        double qRank,
        IEnumerable<double>? weights
    )
    {
        return Math.Sqrt(sigmaSq) / c;
    }

    /// <summary>
    /// Compute the updated ratings for a list of teams in a game.
    /// </summary>
    /// <param name="teams">A list of teams.</param>
    /// <param name="ranks">An optional list representing rank positions for each team.</param>
    /// <param name="scores">An optional list representing scores achieved by each team.</param>
    /// <param name="weights">An optional matrix of player weights applied during the computation.</param>
    /// <returns>A list of teams with updated ratings.</returns>
    protected abstract IEnumerable<ITeam> Compute(
        IList<ITeam> teams,
        IList<double>? ranks = null,
        IList<double>? scores = null,
        IList<IList<double>>? weights = null
    );
}