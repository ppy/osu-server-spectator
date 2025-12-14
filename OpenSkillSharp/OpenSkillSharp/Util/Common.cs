namespace OpenSkillSharp.Util;

/// <summary>
/// Common utility methods for models.
/// </summary>
public static class Common
{
    /// <summary>
    /// Calculates the score for two teams by comparing rank.
    /// </summary>
    /// <param name="q">Team q rank.</param>
    /// <param name="i">Team i rank.</param>
    /// <returns>A number representing the score comparison of the two given ranks.</returns>
    public static double Score(double q, double i)
    {
        return q < i ? 0
            : q > i ? 1
            : 0.5;
    }
}