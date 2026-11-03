using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Paint;
using NecronomiDom.Url;

namespace NecronomiDom.Layout;

public readonly record struct TextMetrics(double Width, double Ascent, double Descent)
{
    /// <summary>Font leading used only by the normal line-height strut; explicit CSS line-height remains authored.</summary>
    public double LineGap { get; init; }
}
public interface ITextShaper { TextMetrics Measure(string text, int fontSize, double fontWeight = 400, double letterSpacing = 0, bool italic = false); }

/// <summary>Logical LTR DOM-fragment boundaries in the retained shape used by measurement and paint.
/// Offsets are UTF-16. Sum whole glyph clusters whose start precedes the boundary, preserving
/// kerning and ligatures; a cluster crossing DOM text nodes belongs to its first node, not half a caret.</summary>
public interface IInlineTextShaper : ITextShaper
{
    double InlineAdvance(string text, int offset, int fontSize, double fontWeight = 400,
        double letterSpacing = 0, bool italic = false);
    Rect InlineInkBounds(string text, int fontSize, double fontWeight = 400,
        double letterSpacing = 0, bool italic = false);
}

/// <summary>
/// Executable bounded flow/relative/absolute CSS layout with final containing-block geometry,
/// block/flow-root, single-line flex and wrapped row flex boxes, decoded images, and definite percentage heights.
/// Ordinary-flow vertical margins remain zero except the body-under-html origin.
/// Produces RGBA box/image/polygon/asymmetric-border runs and ordered elliptical/polygon clip, opacity and 2D-affine contexts.
/// Transformed pointer queries intersect actual inverse viewport pixel quads; navigation uses composed outer viewport bounds.
/// Text uses caller-mapped authored regular families, integer px sizes, font-metric normal or resolved
/// line-height, admitted LTR Unicode wrapping or nowrap, and clipped ellipsis markers.
/// Typed glyph runs retain computed stroke and ordered shadows without changing line geometry;
/// native facilities support 1px/2px strokes and offset/blurred shadows. Font/effect raster fidelity remains incomplete.
/// Inline spans retain per-fragment style, shared shaping, line geometry and DOM text ownership.
/// Mixed block/text, fixed/sticky/z-index and crossing static effect groups are rejected.
/// Wrapped row flex uses independent line breaking/flexible lengths and cross-axis content alignment; column wrapping is rejected.
/// Nonuniform-corner outlines, single-axis rounded clips and 3D effects remain explicit unsupported capabilities.
/// Independent outer ellipses and actual padding-edge overflow ellipses share paint and hit geometry.
/// This is not a complete CSS layout implementation and does not pass the full WPT layout suites.
/// </summary>
public sealed partial class FlowLayout
{
    private readonly StyleEngine _styles;
    private readonly IReadOnlyDictionary<string, ITextShaper> _fontFamilies;
    private readonly IImageSource? _images;
    private readonly CSSStyleDeclaration _initial;
    // The initial profile document never changes, so each property's initial serialization is computed once.
    private readonly Dictionary<string, string> _initialText = new(StringComparer.Ordinal);
    private string InitialText(string property)
    {
        if (!_initialText.TryGetValue(property, out string? text)) _initialText[property] = text = _initial.GetPropertyValue(property);
        return text;
    }
    private DisplayList _result = new();
    public FlowLayout(StyleEngine styles, ITextShaper shaper, string fontFamily, IImageSource? images = null,
        IAnimatedOpacitySource? animatedOpacity = null, IAnimatedBackgroundColorSource? animatedBackgroundColor = null,
        IAnimatedWidthSource? animatedWidth = null, IAnimatedScaleSource? animatedScale = null,
        IAnimatedForegroundColorSource? animatedForegroundColor = null)
        : this(styles, new Dictionary<string, ITextShaper>(StringComparer.OrdinalIgnoreCase) { [fontFamily] = shaper },
            images, animatedOpacity, animatedBackgroundColor, animatedWidth, animatedScale, animatedForegroundColor) { }
    public FlowLayout(StyleEngine styles, IReadOnlyDictionary<string, ITextShaper> fontFamilies, IImageSource? images = null,
        IAnimatedOpacitySource? animatedOpacity = null, IAnimatedBackgroundColorSource? animatedBackgroundColor = null,
        IAnimatedWidthSource? animatedWidth = null, IAnimatedScaleSource? animatedScale = null,
        IAnimatedForegroundColorSource? animatedForegroundColor = null)
    {
        _styles = styles; _fontFamilies = fontFamilies; _images = images;
        _animatedOpacity = animatedOpacity;
        _animatedBackgroundColor = animatedBackgroundColor;
        _animatedWidth = animatedWidth; _animatedScale = animatedScale;
        _animatedForegroundColor = animatedForegroundColor;
        if (animatedForegroundColor != null) _foregroundPaint = new();
        var d = HtmlImporter.Import("<!doctype html><div></div>", "runtime-initial", WebUrl.Parse("res:///runtime-initial.html")!).Document;
        _initial = new StyleEngine(d).GetComputedStyle(d.QuerySelector("div")!);
    }
    public DisplayList Layout(Document document, double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _result = new DisplayList();
        _viewport = new Rect(0, 0, width, height);
        _containing.Clear(); _deferred.Clear();
        _opacityOwners.Clear();
        _transformOwners.Clear();
        _foregroundPaint?.Clear();
        var initial = new PositionedContainingBlock(_viewport, _viewport, true);
        _containing.Add(new ContainingContext(null, initial));
        var rootStyle = _styles.GetComputedStyle(document.DocumentElement!);
        Rgba canvas = SampleBackgroundColor(document.DocumentElement!, Color(rootStyle, "background-color"));
        if (canvas.A == 0 && document.Body != null)
            canvas = SampleBackgroundColor(document.Body, Color(_styles.GetComputedStyle(document.Body), "background-color"));
        _result.Runs.Add(new BoxRun(_viewport, new BoxStyle(canvas, default, 0, 0)));
        BeginPaintPhases(document);
        try
        {
            Block(document.DocumentElement ?? throw new InvalidOperationException("Missing document root."), 0, 0, initial, 0);
            CompletePaintHitOrder();
            CompleteHitGeometry();
            return _result;
        }
        finally { ClearMeasurements(); ReleasePaintPhases(); }
    }
    private double Block(Element e, double x, double y, PositionedContainingBlock flow, int containing,
        PositionedContainingBlock? absolute = null, PositionedBoxPlan? usedPlan = null)
    {
        var s = _styles.GetComputedStyle(e);
        if (s.GetPropertyValue("display") == "none") return 0;
        Validate(e, s);
        if (s.GetPropertyValue("display") is not ("block" or "flow-root" or "flex")) Fail(e, "display", s.GetPropertyValue("display"));
        ImageResource? image = e.LocalName == "img" ? ImageLayout.Intrinsic(_images, e) : null;
        var plan = usedPlan ?? PositionedLayoutSolver.Prepare(s, flow, absolute ?? flow, x, y,
            image == null ? IntrinsicWidth(e, s) : default, image, SampleWidth(e));
        bool rootBody = e.LocalName == "body" && e.ParentElement?.LocalName == "html"
            && ReferenceEquals(e.ParentElement, e.OwnerDocument?.DocumentElement);
        if (usedPlan == null && !plan.ExcludedFromFlow && !rootBody && ((plan.MarginTop ?? 0) != 0 || (plan.MarginBottom ?? 0) != 0))
            Fail(e, "margin-block", "nonzero (collapsing not in this profile)");
        int boxIndex = _result.Boxes.Count, runStart = _result.Runs.Count;
        int contextStart = _containing.Count, deferredStart = _deferred.Count, ownContaining = containing;
        _result.Boxes.Add(new LayoutBox(e, default, s.GetPropertyValue("pointer-events") != "none"));
        var paintFrame = EnterPaintPhase(boxIndex);
        bool transformContext = HasTransform(e, s);
        if (plan.Mode != PositionedMode.Static || transformContext)
        { ownContaining = _containing.Count; _containing.Add(new ContainingContext(e, null)); }
        var gutters = image == null ? ResolveScrollGutters(e, s, plan) : default;
        double natural = image == null ? LayoutChildren(e, s, gutters.Children(plan), ownContaining) : 0;
        var box = gutters.Complete(PositionedLayoutSolver.Complete(plan, natural + gutters.Bottom));
        double dy = box.BorderBox.Y - plan.PreLayoutBorderBoxOriginY;
        if (dy != 0)
        {
            TranslateSubtree(boxIndex + 1, runStart, contextStart, deferredStart, 0, dy);
            TranslateHeldPaint(paintFrame, 0, dy);
        }
        if (plan.Mode != PositionedMode.Static || transformContext)
        {
            _containing[ownContaining] = _containing[ownContaining] with { Box = box.DescendantContainingBlock };
            LayoutDeferred(ownContaining);
        }
        else if (e.ParentElement == null) LayoutDeferred(containing);
        var scrollOverflow = CompleteScrollGeometry(e, s, box, natural, boxIndex, runStart,
            ownContaining == contextStart ? contextStart + 1 : contextStart, paintFrame, gutters);
        var computedValues = _styles.GetComputedValues(e);
        var decoration = BoxDecorationResolver.ResolveParts(computedValues, box.BorderBox);
        decoration = decoration with { Background = SampleBackgroundColor(e, decoration.Background) };
        var context = PositionedLayoutSolver.ResolvePaintContext(computedValues, box, in decoration);
        bool targetOpacityContext = context.GroupOpacity < 1;
        context = SamplePaintOpacity(e, context);
        ConvexPolygon? polygon = s.GetPropertyValue("clip-path") == "none" ? null
            : PolygonClipResolver.Resolve(ComputedValue(s, "clip-path"), box.BorderBox);
        var transform = BoxTransformResolver.Resolve(computedValues, box.BorderBox, SampleScale(e));
        _result.Boxes[boxIndex] = new LayoutBox(e, box.BorderBox, s.GetPropertyValue("pointer-events") != "none")
        { UsedGeometry = new UsedLayoutGeometry(context, polygon, plan.Mode, _containing[containing].Element, decoration.Radii, transform),
            ScrollOverflow = scrollOverflow };
        // Auto/zero positioned phases are scoped and atomic with opacity/clip-path groups.
        // Ordinary ancestors pass positioned descendants outward while retaining their real overflow clips.
        bool paintBoundary = e.ParentElement == null || plan.Mode != PositionedMode.Static
            || targetOpacityContext || context.GroupOpacity < 1 || polygon != null || transform.EstablishesContainingBlock;
        if (paintBoundary) FlushPositionedPaint(paintFrame);
        PaintBox(e, s, computedValues, box, context, polygon, in decoration, image, runStart);
        if (transform.EstablishesContainingBlock)
        {
            var begin = new BeginTransformRun(box.BorderBox, transform.Matrix);
            _result.Runs.Insert(runStart, begin); _transformOwners.Add(begin, e);
            _result.Runs.Add(new EndTransformRun(box.BorderBox));
        }
        CompletePaintPhase(paintFrame, e, plan.Mode, _containing[containing].Element, context, paintBoundary);
        return box.FlowContribution;
    }
    private void Validate(Element e, CSSStyleDeclaration s)
    {
        if (!RuntimeCapabilities.AdmitsElement(e.LocalName)) Fail(e, "element", e.LocalName);
        if (e is HTMLInputElement { IsTextEditable: false } input) Fail(e, "input type", input.Type);
        if (e.ShadowRootInternal != null) Fail(e, "shadow-root", "unsupported");
        List<string>? unsupported = null;
        foreach (var d in s.EnumerateLonghands())
        {
            if (d.Name.StartsWith("--", StringComparison.Ordinal) || RuntimeCapabilities.AdmitsProperty(d.Name)) continue;
            if ((_animatedOpacity != null || _animatedBackgroundColor != null || _animatedWidth != null || _animatedScale != null || _animatedForegroundColor != null)
                && IsTransitionProperty(d.Name)) continue;
            // Prove identical initial serialization directly; unknown/live value classes still serialize below.
            if (ProfileValuesSerializeIdentically(d.Value, _initial.GetSpecifiedValue(d.Name))) continue;
            string value = s.GetPropertyValue(d.Name);
            if (e is HTMLTextAreaElement && d.Name == "overflow-wrap" && value == "break-word") continue;
            // Initial currentcolor changes with inherited color, without enabling the associated effect.
            if (CssPropertyRegistry.Get(d.Name)?.Initial == "currentcolor" && value == s.GetPropertyValue("color")) continue;
            // Perspective origins have no effect without an enabled perspective.
            if (d.Name == "perspective-origin" && s.GetPropertyValue("perspective") == "none") continue;
            if (value != InitialText(d.Name)) (unsupported ??= new()).Add(d.Name + ": " + value);
        }
        if (unsupported != null) Fail(e, "properties", string.Join("; ", unsupported));
        var keywordProperties = RuntimeCapabilities.KeywordProperties;
        for (int i = 0; i < keywordProperties.Count; i++)
        {
            string property = keywordProperties[i];
            if (!RuntimeCapabilities.AdmitsKeyword(property, s.GetPropertyValue(property)))
                Fail(e, property, s.GetPropertyValue(property));
        }
        _ = ResolveTextFamily(s);
        if (s.GetPropertyValue("white-space-collapse") != "collapse"
            && !(e is HTMLTextAreaElement && s.GetPropertyValue("white-space-collapse") == "preserve"))
            Fail(e, "white-space-collapse", s.GetPropertyValue("white-space-collapse"));
    }
    private static string Collapse(string text)
    {
        var b = new StringBuilder(); bool space = false;
        foreach (char c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f') { space = b.Length > 0; continue; }
            if (space) b.Append(' ');
            b.Append(c); space = false;
        }
        return b.ToString();
    }
    private static double Length(CSSStyleDeclaration s, string p, double basis) => Number(s.GetPropertyValue(p), basis);
    private static double Number(string v, double basis)
    {
        if (v == "0") return 0;
        if (v.EndsWith("px", StringComparison.Ordinal) && double.TryParse(v.AsSpan(0, v.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return n;
        if (v.EndsWith('%') && double.TryParse(v.AsSpan(0, v.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n * basis / 100;
        throw new NotSupportedException("Runtime length is not resolved px/percentage: " + v);
    }
    private static Rgba Color(CSSStyleDeclaration s, string property)
    {
        if (s.GetSpecifiedValue(property) is ComputedValueColor c)
        { var rgb = c.Color.ToSrgb(); return new Rgba((float)rgb.C1, (float)rgb.C2, (float)rgb.C3, (float)rgb.Alpha); }
        throw new NotSupportedException("Runtime requires resolved sRGB color: " + property + "=" + s.GetPropertyValue(property));
    }
    private static void Fail(Element e, string p, string value) => throw new NotSupportedException($"Runtime profile rejects <{e.LocalName} id='{e.Id}'> {p}: {value}");
}
