namespace OpenSkillSharp.Rating;

/// <summary>
/// Interfaces an object representing a team of player ratings.
/// </summary>
public interface ITeam
{
    /// <summary>
    /// A list of ratings representing the players on the team.
    /// </summary>
    public IEnumerable<IRating> Players { get; set; }

    /// <summary>
    /// Creates a deep copy of the team object.
    /// All <see cref="Players"/> are also cloned during the process.
    /// </summary>
    /// <returns>A deep copy of the team object.</returns>
    public ITeam Clone();
}