namespace OpenSkillSharp.Rating;

public class Rating : IRating
{
    public double Mu { get; set; } = 25D;

    public double Sigma { get; set; } = 25D / 3;

    public double Ordinal => GetOrdinal();

    public IRating Clone()
    {
        return new Rating { Mu = Mu, Sigma = Sigma };
    }

    public double GetOrdinal(double z = 3, double alpha = 1, double target = 0)
    {
        return alpha * (Mu - (z * Sigma) + (target / alpha));
    }
}