using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests.TestingUtil;

public static class Assertions
{
    public static void RatingResultsEqual(IEnumerable<ITeam> expected, IEnumerable<ITeam> actual)
    {
        foreach ((int teamIdx, ITeam expectedTeam) in expected.Index())
        {
            foreach ((int playerIdx, IRating expectedPlayer) in expectedTeam.Players.Index())
            {
                RatingsEqual(expectedPlayer, actual.ElementAt(teamIdx).Players.ElementAt(playerIdx));
            }
        }
    }

    public static void RatingsEqual(IRating expected, IRating actual)
    {
        Assert.Equal(expected.Mu, actual.Mu, 0.0001);
        Assert.Equal(expected.Sigma, actual.Sigma, 0.0001);
    }
}