namespace SpaceSharp.Services;

/// <summary>The numbers behind the drive tile, which is the logo: a cube with a hollow, and a small cube in the hollow that grows with
/// how full the drive is.</summary>
public static class DriveGauge
{
    /// <summary>The small cube's size for an empty drive, as a multiple of its size in the logo: small, but still a cube.</summary>
    public const double EmptyScale = 0.45;

    /// <summary>The small cube's size for a full drive: it reaches the hollow's walls without touching them.</summary>
    public const double FullScale = 2.2;

    /// <summary>How large to draw the small cube for a drive.</summary>
    /// <param name="usedFraction">The used share of the drive, 0 to 1; anything outside, or not a number, is taken as the nearer end.</param>
    /// <returns>A scale from <see cref="EmptyScale"/> to <see cref="FullScale"/>, growing evenly with the used share.</returns>
    public static double InnerScale(double usedFraction)
    {
        double share = double.IsNaN(usedFraction) ? 0 : Math.Clamp(usedFraction, 0, 1);
        return EmptyScale + ((FullScale - EmptyScale) * share);
    }
}
