using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests;

public class PredictWinTests
{
    private const double Tolerance = 0.0000001;
    private readonly IRating _a1;
    private readonly IRating _a2;
    private readonly IRating _b2;
    private readonly IOpenSkillModel _model;
    private readonly ITeam _team1;
    private readonly ITeam _team2;

    public PredictWinTests()
    {
        _model = new PlackettLuce();
        _a1 = _model.Rating();
        _a2 = _model.Rating(32.444, 5.123);
        IRating b1 = _model.Rating(73.381, 1.421);
        _b2 = _model.Rating(25.188, 6.211);
        _team1 = new Team { Players = [_a1, _a2] };
        _team2 = new Team { Players = [b1, _b2] };
    }

    [Theory]
    [InlineData(2, 0.5)]
    [InlineData(3, 0.333333333333)]
    [InlineData(4, 0.25)]
    [InlineData(5, 0.2)]
    public void PredictWin_FFA_NewPlayers(int nTeams, double expectedProbability)
    {
        List<ITeam> teams = Enumerable
            .Range(0, nTeams)
            .Select(_ => new Team { Players = [_a1] })
            .Cast<ITeam>()
            .ToList();

        List<double> probabilities = _model.PredictWin(teams).ToList();

        Assert.All(probabilities, prob => Assert.Equal(expectedProbability, prob, Tolerance));
        Assert.Equal(1, probabilities.Sum(), Tolerance);
    }

    [Fact]
    public void PredictWin_2Teams()
    {
        List<double> probabilities = _model.PredictWin([_team1, _team2]).ToList();

        Assert.Equal(0.0008308945674377, probabilities.ElementAt(0), Tolerance);
        Assert.Equal(0.9991691054325622, probabilities.ElementAt(1), Tolerance);
        Assert.Equal(1, probabilities.Sum(), Tolerance);
    }

    [Fact]
    public void PredictWin_MultipleAsymmetricTeams()
    {
        List<double> probabilities = _model.PredictWin([
            _team1,
            _team2,
            new Team { Players = [_a2] },
            new Team { Players = [_b2] }
        ]).ToList();

        Assert.Equal(0.32579822053781543, probabilities.ElementAt(0), Tolerance);
        Assert.Equal(0.49965489287103865, probabilities.ElementAt(1), Tolerance);
        Assert.Equal(0.12829642754274315, probabilities.ElementAt(2), Tolerance);
        Assert.Equal(0.04625045904840272, probabilities.ElementAt(3), Tolerance);
        Assert.Equal(1, probabilities.Sum(), Tolerance);
    }

    [Fact]
    public void PredictWin_4PlayerFFA_VaryingSkill()
    {
        List<double> probabilities = _model.PredictWin([
            new Team { Players = [_model.Rating(1, 0.1)] },
            new Team { Players = [_model.Rating(2, 0.1)] },
            new Team { Players = [_model.Rating(3, 0.1)] },
            new Team { Players = [_model.Rating(4, 0.1)] }
        ]).ToList();

        Assert.Equal(0.20281164759988402, probabilities.ElementAt(0), Tolerance);
        Assert.Equal(0.2341964232088598, probabilities.ElementAt(1), Tolerance);
        Assert.Equal(0.2658035767911402, probabilities.ElementAt(2), Tolerance);
        Assert.Equal(0.297188352400116, probabilities.ElementAt(3), Tolerance);
        Assert.Equal(1, probabilities.Sum(), Tolerance);
    }

    [Fact]
    public void PredictWin_5PlayerFFA_WithImposter()
    {
        List<double> probabilities = _model.PredictWin([
            new Team { Players = [_a1] },
            new Team { Players = [_a1] },
            new Team { Players = [_a1] },
            new Team { Players = [_a2] },
            new Team { Players = [_a1] }
        ]).ToList();

        Assert.Equal(0.1790804191839367, probabilities.ElementAt(0), Tolerance);
        Assert.Equal(0.1790804191839367, probabilities.ElementAt(1), Tolerance);
        Assert.Equal(0.1790804191839367, probabilities.ElementAt(2), Tolerance);
        Assert.Equal(0.2836783412642534, probabilities.ElementAt(3), Tolerance);
        Assert.Equal(0.1790804191839367, probabilities.ElementAt(4), Tolerance);
        Assert.Equal(1, probabilities.Sum(), Tolerance);
    }
}