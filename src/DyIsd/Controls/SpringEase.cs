using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DyIsd.Controls;

/// <summary>
/// Apple-style spring: moves fast, overshoots a little (about 6%), then settles.
/// A damped cosine: 1 - e^(-damping·t) · cos(frequency·t).
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public double Damping { get; set; } = 8;
    public double Frequency { get; set; } = 9;

    public SpringEase() => EasingMode = EasingMode.EaseIn; // use the curve exactly as written

    protected override double EaseInCore(double t) =>
        t >= 1 ? 1 : 1 - Math.Exp(-Damping * t) * Math.Cos(Frequency * t);

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
