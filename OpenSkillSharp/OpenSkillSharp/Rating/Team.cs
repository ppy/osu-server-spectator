namespace OpenSkillSharp.Rating;

public class Team : ITeam
{
    public IEnumerable<IRating> Players { get; set; } = new List<IRating>();

    public virtual ITeam Clone()
    {
        return new Team { Players = Players.Select(p => p.Clone()).ToList() };
    }
}