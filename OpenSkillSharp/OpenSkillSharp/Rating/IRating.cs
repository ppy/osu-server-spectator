namespace OpenSkillSharp.Rating;

/// <summary>
/// Interfaces an object representing player rating data.
/// </summary>
public interface IRating
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
    /// A single scalar value that represents the player's skill where their true skill is 99.7% likely to be higher.
    /// </summary>
    public double Ordinal { get; }

    /// <summary>
    /// Calculates a single scalar value that represents the player's skill where
    /// their true skill is 99.7% likely to be higher.
    /// </summary>
    /// <param name="z">
    /// A number that represents the number of standard deviations to subtract from the mean.
    /// By default, this value is 3.0, which corresponds to a 99.7% confidence interval in a normal distribution.
    /// </param>
    /// <param name="alpha">
    /// A number that represents a scaling factor applied to the entire calculation.
    /// Adjusts the overall scale of the ordinal value.
    /// </param>
    /// <param name="target">
    /// A number used to shift the ordinal value towards a specific target.
    /// The shift is adjusted by the <paramref name="alpha"/> scaling factor.
    /// </param>
    /// <returns>An ordinal value calculated for the player.</returns>
    public double GetOrdinal(double z = 3, double alpha = 1, double target = 0);

    /// <summary>
    /// Creates a deep copy of the rating object.
    /// </summary>
    /// <returns>A deep copy of the rating object.</returns>
    public IRating Clone();
}