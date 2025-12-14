using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;

namespace OpenSkillSharp;

public interface IOpenSkillModel
{
    /// <summary>
    /// Represents the initial belief about the skill of a player before any matches have been played.
    /// Known mostly as the mean of the Gaussian prior distribution.
    /// </summary>
    public double Mu { get; set; }

    /// <summary>
    /// Standard deviation of the prior distribution of the player.
    /// </summary>
    public double Sigma { get; set; }

    /// <summary>
    /// Hyperparameter that determines the level of uncertainty or variability present in the prior
    /// distribution of ratings.
    /// </summary>
    public double Beta { get; set; }

    /// <summary>
    /// The value of <see cref="Beta"/> squared.
    /// </summary>
    public double BetaSq { get; }

    /// <summary>
    /// Arbitrary, small, positive, real number that is used to prevent the variance of the posterior distribution
    /// from becoming too small or negative. It can also be thought of as a regularization parameter.
    /// </summary>
    public double Kappa { get; set; }

    /// <summary>
    /// Function used as the formula to calculate gamma.
    /// </summary>
    public GammaFactory Gamma { get; set; }

    /// <summary>
    /// Additive dynamics parameter that prevents sigma from getting too small to increase rating change volatility.
    /// </summary>
    public double Tau { get; set; }

    /// <summary>
    /// The margin of victory needed for a win to be considered impressive.
    /// </summary>
    public double Margin { get; set; }

    /// <summary>
    /// Determines whether to restrict the value of sigma from increasing.
    /// </summary>
    public bool LimitSigma { get; set; }

    /// <summary>
    /// Determines whether to emphasize rating outliers.
    /// </summary>
    public bool Balance { get; set; }

    /// <summary>
    /// Creates a new rating object with the configured defaults for this model. The given parameters can
    /// override the defaults for this model, but it is not recommended unless you know what you are doing.
    /// </summary>
    /// <param name="mu">
    /// Represents the initial belief about the skill of a player before any matches have been played.
    /// Known mostly as the mean of the Gaussian prior distribution.
    /// </param>
    /// <param name="sigma">
    /// Standard deviation of the prior distribution of the player.
    /// </param>
    /// <returns>A new rating object.</returns>
    public IRating Rating(double? mu = null, double? sigma = null);

    /// <summary>
    /// Calculate the new ratings based on the given teams and parameters.
    /// </summary>
    /// <param name="teams">A list of teams.</param>
    /// <param name="ranks">
    /// A list of numbers corresponding to the given <paramref name="teams"/> where lower values represent winners.
    /// </param>
    /// <param name="scores">
    /// A list of numbers corresponding to the given <paramref name="teams"/> where higher values represent winners.
    /// </param>
    /// <param name="weights">
    /// A list of lists of numbers corresponding to the given <paramref name="teams"/>
    /// where each inner list represents the contribution of each player to the team's performance.
    /// </param>
    /// <param name="tau">
    /// Additive dynamics parameter that prevents sigma from getting too small to increase rating change volatility.
    /// </param>
    /// <returns>
    /// A list of teams where each team contains a list of updated rating objects.
    /// </returns>
    public IEnumerable<ITeam> Rate(
        IList<ITeam> teams,
        IList<double>? ranks = null,
        IList<double>? scores = null,
        IList<IList<double>>? weights = null,
        double? tau = null
    );

    /// <summary>
    /// Predict the likelihood of each team to win a match against teams of one or more players.
    /// </summary>
    /// <param name="teams">A list of two or more teams.</param>
    /// <returns>A list of numbers representing the odds of each team winning.</returns>
    public IEnumerable<double> PredictWin(IList<ITeam> teams);

    /// <summary>
    /// Predict how likely a match of teams of one or more players will conclude in a draw.
    /// </summary>
    /// <param name="teams">A list of two or more teams.</param>
    /// <returns>A number representing the odds of a draw as a percentage from 0.0 to 1.0</returns>
    public double PredictDraw(IList<ITeam> teams);
}