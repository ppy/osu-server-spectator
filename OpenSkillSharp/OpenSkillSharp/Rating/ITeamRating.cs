namespace OpenSkillSharp.Rating;

/// <summary>
/// Interfaces an object representing the collective rating of a team.
/// </summary>
public interface ITeamRating : ITeam
{
    /// <summary>
    /// Represents the initial belief about the collective skill of a team before any matches have been played.
    /// Known mostly as the mean of the Gaussian prior distribution.
    /// </summary>
    public double Mu { get; set; }

    /// <summary>
    /// Standard deviation of the prior distribution of a team.
    /// </summary>
    public double SigmaSq { get; set; }

    /// <summary>
    /// The rank of the team within a game
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Creates a deep copy of the team rating object.
    /// All <see cref="ITeam.Players"/> are also cloned during the process.
    /// </summary>
    /// <returns>A deep copy of the team rating object.</returns>
    public new ITeamRating Clone();
}