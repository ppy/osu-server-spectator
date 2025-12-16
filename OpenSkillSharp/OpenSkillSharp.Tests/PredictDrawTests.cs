using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests;

public class PredictDrawTests
{
    private const double Tolerance = 0.0000001;
    private readonly IOpenSkillModel _model = new PlackettLuce();

    [Fact]
    public void PredictDraw_PythonTestParity()
    {
        double probability = _model.PredictDraw([
            new Team { Players = [_model.Rating(25, 1), _model.Rating(25, 1)] },
            new Team { Players = [_model.Rating(25, 1), _model.Rating(25, 1)] }
        ]);

        Assert.Equal(0.2433180271619435, probability, Tolerance);
    }

    [Fact]
    public void PredictDraw_GivenUnevenTeams_ProducesLowerProbability()
    {
        double probability = _model.PredictDraw([
            new Team { Players = [_model.Rating(35, 1), _model.Rating(35, 1)] },
            new Team { Players = [_model.Rating(35, 1), _model.Rating(35, 1), _model.Rating(35, 1)] }
        ]);

        Assert.Equal(0.0002807397636509501, probability, Tolerance);
    }

    [Fact]
    public void PredictDraw_Given1v1OfSimilarSkill_ProducesHigherProbability()
    {
        double probability = _model.PredictDraw([
            new Team { Players = [_model.Rating(35, 1)] },
            new Team { Players = [_model.Rating(35, 1.1)] }
        ]);

        Assert.Equal(0.4868868769871696, probability, Tolerance);
    }

    [Fact]
    public void PredictDraw_Given5thDefectorSittingOut_ProducesHighProbability()
    {
        double probability = _model.PredictDraw([
            new Team
            {
                Players =
                [
                    _model.Rating(28.450555874288018, 8.156810439252277),
                    _model.Rating(28.450555874288018, 8.156810439252277)
                ]
            },
            new Team
            {
                Players =
                [
                    _model.Rating(23.096623784758727, 8.138233582011868),
                    _model.Rating(21.537948364040137, 8.155255551436932)
                ]
            }
        ]);

        Assert.Equal(0.09227283302635064, probability, Tolerance);
    }
}