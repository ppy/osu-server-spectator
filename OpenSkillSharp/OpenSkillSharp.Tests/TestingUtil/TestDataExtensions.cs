using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests.TestingUtil;

public static class TestDataExtensions
{
    public static IList<ITeam> MockTeams(this IOpenSkillModel model, IList<ITeam> teams)
    {
        return teams.Select(t => new Team { Players = t.Players.Select(_ => model.Rating()) }).Cast<ITeam>().ToList();
    }
}