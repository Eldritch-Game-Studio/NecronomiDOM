using System;
using System.Globalization;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

internal enum BoxLengthKind : byte { Px, Percent, Auto, None, Normal, Content, Other }

/// <summary>A computed length-percentage or sizing keyword parsed once per computed-style version.
/// <see cref="BoxLengthKind.Other"/> keeps the consumer's own rejection: the snapshot never throws for a
/// property that the current layout path would not have read.</summary>
internal readonly struct BoxLength
{
    internal readonly double Value;
    internal readonly BoxLengthKind Kind;
    private BoxLength(double value, BoxLengthKind kind) { Value = value; Kind = kind; }
    internal bool IsLength => Kind <= BoxLengthKind.Percent;
    internal bool IsPercent => Kind == BoxLengthKind.Percent;
    internal bool IsAuto => Kind == BoxLengthKind.Auto;
    internal bool IsNone => Kind == BoxLengthKind.None;
    /// <summary>Resolves a length-percentage; callers check <see cref="IsLength"/> first.</summary>
    internal double Resolve(double basis) => Kind == BoxLengthKind.Percent ? Value * basis / 100 : Value;
    internal static BoxLength Parse(string value)
    {
        switch (value)
        {
            case "0": return new(0, BoxLengthKind.Px);
            case "auto": return new(0, BoxLengthKind.Auto);
            case "none": return new(0, BoxLengthKind.None);
            case "normal": return new(0, BoxLengthKind.Normal);
            case "content": return new(0, BoxLengthKind.Content);
        }
        if (value.EndsWith("px", StringComparison.Ordinal)
            && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && double.IsFinite(number)) return new(number, BoxLengthKind.Px);
        if (value.EndsWith('%')
            && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number)) return new(number, BoxLengthKind.Percent);
        return new(0, BoxLengthKind.Other);
    }
    internal static BoxLength FromValue(CssValue value) => value switch
    {
        CssDimensionValue { Unit: "px" } px when double.IsFinite(px.Value) => new(px.Value, BoxLengthKind.Px),
        CssNumberValue { Value: 0 } => new(0, BoxLengthKind.Px),
        CssPercentageValue percentage when double.IsFinite(percentage.Value) => new(percentage.Value, BoxLengthKind.Percent),
        _ => new(0, BoxLengthKind.Other)
    };
}

/// <summary>Layout's parsed view of one element's computed style. Built from the computed CSSOM text and typed
/// values once per <see cref="CSSStyleDeclaration.Version"/>, then shared by every flex measurement pass, the
/// positioned solver and the box decoration resolver instead of re-reading and re-parsing ~30 property strings
/// per box per pass. Values keep the supported-profile semantics of the string readers they replace; invalid
/// or unsupported inputs are retained as such so the consuming path raises the same rejection it always did.</summary>
internal sealed class BoxStyleSnapshot
{
    internal readonly CSSStyleDeclaration Style;
    internal readonly int Version;

    internal readonly string Display, Position, FlexDirection, FlexWrap, AlignItems, AlignSelf, JustifyContent, Order,
        FlexBasisText, OverflowX, OverflowY, TextWrapMode;
    /// <summary>Null when position is outside the static/relative/absolute profile.</summary>
    internal readonly PositionedMode? Mode;
    internal readonly bool DirectionLtr, ZIndexAuto, BoxSizingValid, BorderBox, PointerEvents, ClipPathNone, DisplayNone;
    internal readonly BoxLength PaddingTop, PaddingRight, PaddingBottom, PaddingLeft,
        BorderTopWidth, BorderRightWidth, BorderBottomWidth, BorderLeftWidth,
        MarginTop, MarginRight, MarginBottom, MarginLeft, Left, Right, Top, Bottom,
        Width, Height, MinWidth, MaxWidth, MinHeight, MaxHeight, FlexBasis, ColumnGap, RowGap;
    /// <summary>Resolved padding and border edges when none is a percentage; otherwise null.</summary>
    internal readonly PositionedEdges? FixedEdges;
    /// <summary>NaN when the computed factor is not a finite non-negative number.</summary>
    internal readonly double FlexGrow, FlexShrink;
    /// <summary>NaN when the computed opacity is not a plain number.</summary>
    internal readonly double Opacity;
    /// <summary>Any of transform/translate/rotate/scale is not none.</summary>
    internal readonly bool HasTransform;
    private string? _fontFamily;

    private BorderSides _borders;
    private bool _bordersResolved;
    private Rgba? _background;
    private readonly BoxLength[] _cornerLengths = new BoxLength[8];
    private readonly CssValue[] _cornerValues = new CssValue[8];
    private bool _cornersResolved;
    /// <summary>Id of the FlowLayout instance that proved this style against the runtime profile (its own animation
    /// sources and initial-value map); 0 until proved. An id, not a reference, so a replaced layout is not retained.</summary>
    internal int ValidatedBy;

    private BoxStyleSnapshot(CSSStyleDeclaration style, int version)
    {
        Style = style; Version = version;
        Display = style.GetPropertyValue("display");
        DisplayNone = Display == "none";
        Position = style.GetPropertyValue("position");
        Mode = Position switch
        {
            "static" => PositionedMode.Static, "relative" => PositionedMode.Relative, "absolute" => PositionedMode.Absolute, _ => null
        };
        DirectionLtr = style.GetPropertyValue("direction") == "ltr";
        ZIndexAuto = style.GetPropertyValue("z-index") == "auto";
        string sizing = style.GetPropertyValue("box-sizing");
        BoxSizingValid = sizing is "content-box" or "border-box";
        BorderBox = sizing == "border-box";
        PointerEvents = style.GetPropertyValue("pointer-events") != "none";
        ClipPathNone = style.GetPropertyValue("clip-path") == "none";
        PaddingTop = Length(style, "padding-top"); PaddingRight = Length(style, "padding-right");
        PaddingBottom = Length(style, "padding-bottom"); PaddingLeft = Length(style, "padding-left");
        BorderTopWidth = Length(style, "border-top-width"); BorderRightWidth = Length(style, "border-right-width");
        BorderBottomWidth = Length(style, "border-bottom-width"); BorderLeftWidth = Length(style, "border-left-width");
        if (PaddingTop.Kind == BoxLengthKind.Px && PaddingRight.Kind == BoxLengthKind.Px && PaddingBottom.Kind == BoxLengthKind.Px
            && PaddingLeft.Kind == BoxLengthKind.Px && BorderTopWidth.Kind == BoxLengthKind.Px && BorderRightWidth.Kind == BoxLengthKind.Px
            && BorderBottomWidth.Kind == BoxLengthKind.Px && BorderLeftWidth.Kind == BoxLengthKind.Px)
            FixedEdges = new PositionedEdges(PaddingTop.Value, PaddingRight.Value, PaddingBottom.Value, PaddingLeft.Value,
                BorderTopWidth.Value, BorderRightWidth.Value, BorderBottomWidth.Value, BorderLeftWidth.Value);
        MarginTop = Length(style, "margin-top"); MarginRight = Length(style, "margin-right");
        MarginBottom = Length(style, "margin-bottom"); MarginLeft = Length(style, "margin-left");
        Left = Length(style, "left"); Right = Length(style, "right"); Top = Length(style, "top"); Bottom = Length(style, "bottom");
        Width = Length(style, "width"); Height = Length(style, "height");
        MinWidth = Length(style, "min-width"); MaxWidth = Length(style, "max-width");
        MinHeight = Length(style, "min-height"); MaxHeight = Length(style, "max-height");
        FlexDirection = style.GetPropertyValue("flex-direction");
        FlexWrap = style.GetPropertyValue("flex-wrap");
        AlignItems = style.GetPropertyValue("align-items");
        AlignSelf = style.GetPropertyValue("align-self");
        JustifyContent = style.GetPropertyValue("justify-content");
        Order = style.GetPropertyValue("order");
        FlexBasisText = style.GetPropertyValue("flex-basis");
        FlexBasis = BoxLength.Parse(FlexBasisText);
        ColumnGap = Length(style, "column-gap"); RowGap = Length(style, "row-gap");
        FlexGrow = Factor(style, "flex-grow"); FlexShrink = Factor(style, "flex-shrink");
        OverflowX = style.GetPropertyValue("overflow-x"); OverflowY = style.GetPropertyValue("overflow-y");
        TextWrapMode = style.GetPropertyValue("text-wrap-mode");
        Opacity = style.GetSpecifiedValue("opacity") is CssNumberValue opacity ? opacity.Value : double.NaN;
        HasTransform = !None("transform") || !None("translate") || !None("rotate") || !None("scale");
        bool None(string property) => style.GetSpecifiedValue(property) is CssKeywordValue { Value: "none" };
    }

    /// <summary>The current snapshot for a computed style, rebuilt only after the declaration changed.</summary>
    internal static BoxStyleSnapshot For(CSSStyleDeclaration style)
    {
        int version = style.Version;
        if (style.ConsumerView is BoxStyleSnapshot snapshot && snapshot.Version == version) return snapshot;
        snapshot = new BoxStyleSnapshot(style, version);
        style.ConsumerView = snapshot;
        return snapshot;
    }

    private static BoxLength Length(CSSStyleDeclaration style, string property) => BoxLength.Parse(style.GetPropertyValue(property));
    private static double Factor(CSSStyleDeclaration style, string property)
        => double.TryParse(style.GetPropertyValue(property), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) && value >= 0 ? value : double.NaN;

    /// <summary>Computed border sides (width, color, line style), resolved on first paint of this version.</summary>
    internal ref readonly BorderSides Borders
    {
        get
        {
            if (!_bordersResolved)
            {
                _borders = new BorderSides(
                    Side("border-top-width", "border-top-color", "border-top-style"),
                    Side("border-right-width", "border-right-color", "border-right-style"),
                    Side("border-bottom-width", "border-bottom-color", "border-bottom-style"),
                    Side("border-left-width", "border-left-color", "border-left-style"));
                _bordersResolved = true;
            }
            return ref _borders;
        }
    }
    internal Rgba Background => _background ??= Color("background-color");
    /// <summary>The authored family name layout maps to a shaper (AuthoredFontFamilies.Name), resolved once per version.</summary>
    internal string FontFamily => _fontFamily ??= AuthoredFontFamilies.Name(Style.GetSpecifiedValue("font-family"));
    /// <summary>Border radii resolved against the final border box, before overlap normalization.</summary>
    internal BorderRadii Radii(Rect bounds)
    {
        if (!_cornersResolved)
        {
            Corner(0, "border-top-left-radius"); Corner(1, "border-top-right-radius");
            Corner(2, "border-bottom-right-radius"); Corner(3, "border-bottom-left-radius");
            _cornersResolved = true;
        }
        return new BorderRadii(CornerAt(0, bounds), CornerAt(1, bounds), CornerAt(2, bounds), CornerAt(3, bounds));
    }
    private BorderCorner CornerAt(int corner, Rect bounds)
        => new(Math.Max(0, CornerLength(corner * 2, bounds.Width)), Math.Max(0, CornerLength(corner * 2 + 1, bounds.Height)));
    private double CornerLength(int slot, double basis)
    {
        var length = _cornerLengths[slot];
        return length.Kind == BoxLengthKind.Other ? UsedCssNumeric.Length(_cornerValues[slot], basis) : length.Resolve(basis);
    }
    private void Corner(int corner, string property)
    {
        CssValue value = Required(property);
        CssValue x = value, y = value;
        if (value is CssListValue list)
        {
            if (list.Items.Count != 2) throw new NotSupportedException("Unresolved border corner radius.");
            x = list.Items[0]; y = list.Items[1];
        }
        _cornerValues[corner * 2] = x; _cornerValues[corner * 2 + 1] = y;
        _cornerLengths[corner * 2] = BoxLength.FromValue(x); _cornerLengths[corner * 2 + 1] = BoxLength.FromValue(y);
    }
    private BorderSide Side(string widthProperty, string colorProperty, string styleProperty)
    {
        string lineStyle = (Style.GetSpecifiedValue(styleProperty) as CssKeywordValue)?.Value
            ?? throw new NotSupportedException("Runtime requires computed border style: " + styleProperty);
        if (lineStyle is not ("none" or "hidden" or "solid"))
            throw new NotSupportedException("Runtime border style is unavailable: " + lineStyle);
        double width = lineStyle is "none" or "hidden" ? 0 : UsedCssNumeric.Length(Required(widthProperty), 0);
        if (width < 0) throw new NotSupportedException("Negative used border width.");
        return new(width, Color(colorProperty), lineStyle);
    }
    private CssValue Required(string property) => Style.GetSpecifiedValue(property)
        ?? throw new NotSupportedException("Missing computed property: " + property);
    private Rgba Color(string property)
    {
        if (Required(property) is not ComputedValueColor color)
            throw new NotSupportedException("Runtime requires resolved color: " + property);
        var rgb = color.Color.ToSrgb();
        return new((float)Math.Clamp(rgb.C1, 0, 1), (float)Math.Clamp(rgb.C2, 0, 1), (float)Math.Clamp(rgb.C3, 0, 1), (float)Math.Clamp(rgb.Alpha, 0, 1));
    }
}
