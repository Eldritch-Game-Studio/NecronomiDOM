using System.Collections.Generic;
using NecronomiDom.Css.Values.Colors;

namespace NecronomiDom.Paint;

public enum GradientKind { Linear, Radial, Conic }
public readonly record struct GradientStop(double Position, CssResolvedColor Color, double? HintAfter = null);

/// <summary>Used CSS gradient geometry in tile-local CSS pixels; immutable, resolved, and engine independent.</summary>
/// <remarks>
/// Linear: X/Y is the 0% endpoint, AxisX/Y the unit direction, Length the 100% distance.
/// Radial: X/Y is the center, AxisX/Y the ending radii, Length the horizontal radius.
/// Conic: X/Y is the center, AxisX the starting rotation in turns, Length is one turn.
/// Stop positions and hints are normalized against Length; they may lie outside [0,1].
/// Circular distinguishes zero-radius circles from degenerate ellipses.
/// </remarks>
public sealed record GradientImage(GradientKind Kind, bool Repeating, double X, double Y,
    double AxisX, double AxisY, double Length, string InterpolationSpace, string HueMethod,
    IReadOnlyList<GradientStop> Stops, bool Circular);

/// <summary>One generated background image. Tile phase is independent of its visible clipping edge.</summary>
public sealed record GradientRun(GradientImage Image, Rect Tile, EllipticalClipGeometry Clip,
    double StepX, double StepY, bool RepeatX, bool RepeatY) : PaintRun;
