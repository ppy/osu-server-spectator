using System.Diagnostics.CodeAnalysis;

using MathNet.Numerics.Distributions;

namespace OpenSkillSharp.Util;

/// <summary>
/// Provides statistical utilities for solving values based on the standard normal distribution.
/// </summary>
public static class Statistics
{
    private static readonly Normal Normal = new(0, 1);
    private const double Epsilon = 1e-10;
    
    /// <summary>
    /// Computes the cumulative distribution (CDF) of the standard normal distribution at the given number.
    /// </summary>
    /// <param name="x">A number representing the location for which to evaluate the CDF.</param>
    /// <returns>
    /// The probability that a random variable from the standard normal distribution is less than or equal to
    /// <paramref name="x"/> represented as a number between 0 and 1.
    /// </returns>
    public static double PhiMajor(double x)
    {
        return Normal.CumulativeDistribution(x);
    }

    /// <summary>
    /// Computes the inverse of the cumulative distribution function (InvCDF) for the standard normal distribution at
    /// the given probability.
    /// </summary>
    /// <param name="x">A number representing the location for which to evaluate the InvCDF.</param>
    /// <returns>The inverse cumulative density at <paramref name="x"/>.</returns>
    public static double InversePhiMajor(double x)
    {
        return Normal.InverseCumulativeDistribution(x);
    }
    
    /// <summary>
    /// Computes the probability density of the standard normal distribution (PDF) at the given number.
    /// </summary>
    /// <param name="x">A number representing the location at which to compute the density.</param>
    /// <returns>The probability density at <paramref name="x"/>.</returns>
    public static double PhiMinor(double x)
    {
        return Normal.Density(x);
    }

    /// <summary>
    /// The function <c>V</c> as defined in
    /// <a href="https://jmlr.org/papers/v12/weng11a.html"><c>JMLR:v12:weng11a</c></a>.
    /// <br/>
    /// Represented by:
    /// <br/>
    /// <c>V (x,t) = φ(x − t)/Φ(x − t)</c>
    /// </summary>
    /// <param name="x">A number.</param>
    /// <param name="t">A number.</param>
    /// <returns>A number.</returns>
    public static double V(double x, double t)
    {
        double xt = x - t;
        double denominator = PhiMajor(xt);

        return denominator < Epsilon
            ? -xt
            : PhiMinor(xt) / denominator;
    }

    /// <summary>
    /// The function <c>W</c> as defined in
    /// <a href="https://jmlr.org/papers/v12/weng11a.html"><c>JMLR:v12:weng11a</c></a>.
    /// <br/>
    /// Represented by:
    /// <br/>
    /// <c>W (x,t) = V (x,t)(V (x,t) + (x − t))</c>
    /// </summary>
    /// <param name="x">A number.</param>
    /// <param name="t">A number.</param>
    /// <returns>A number.</returns>
    public static double W(double x, double t)
    {
        double xt = x - t;
        double denominator = PhiMajor(xt);

        if (denominator < Epsilon)
        {
            return x < 0 ? 1 : 0;
        }

        return V(x, t) * (V(x, t) + xt);
    }

    /// <summary>
    /// The function <c>~V</c> as defined in
    /// <a href="https://jmlr.org/papers/v12/weng11a.html"><c>JMLR:v12:weng11a</c></a>.
    /// <br/>
    /// Represented by:
    /// <br/>
    /// <c>~V (x,t) = −((φ(t − x) − φ(−t − x)) / (Φ(t − x) − Φ(−t − x)))</c>
    /// </summary>
    /// <param name="x">A number.</param>
    /// <param name="t">A number.</param>
    /// <returns>A number.</returns>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static double VT(double x, double t)
    {
        double xx = Math.Abs(x);
        double b = PhiMajor(t - xx) - PhiMajor(-t - xx);

        if (b < Epsilon)
        {
            return x < 0
                ? -x - t
                : -x + t;
        }

        double a = PhiMinor(-t - xx) - PhiMinor(t - xx);
        return (x < 0 ? -a : a) / b;
    }

    /// <summary>
    /// The function <c>~W</c> as defined in
    /// <a href="https://jmlr.org/papers/v12/weng11a.html"><c>JMLR:v12:weng11a</c></a>.
    /// <br/>
    /// Represented by:
    /// <br/>
    /// <c>~W (x,t) = (((t − x)φ(t − x) − (−(t + x))φ(−(t + x))) /  (Φ(t − x) − Φ(−t − x))) + ~V(x,t)^2</c>
    /// </summary>
    /// <param name="x">A number.</param>
    /// <param name="t">A number.</param>
    /// <returns>A number.</returns>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static double WT(double x, double t)
    {
        double xx = Math.Abs(x);
        double b = PhiMajor(t - xx) - PhiMajor(-t - xx);

        return b < double.Epsilon
            ? 1
            : ((((t - xx) * PhiMinor(t - xx)) + ((t + xx) * PhiMinor(-t - xx))) / b) + (VT(x, t) * VT(x, t));
    }
}