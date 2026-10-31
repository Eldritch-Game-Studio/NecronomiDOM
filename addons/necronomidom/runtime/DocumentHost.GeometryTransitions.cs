using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Layout;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Immutable width/scale transition profile; ordinary documents retain CSS semantics.</summary>
    public GeometryTransitionProfile GeometryTransitionProfile
    {
        get => _widthController.Profile;
        init { _widthController = new(value); _scaleController = new(value); }
    }
    public bool HasRunningWidthTransitions => _widthController.HasRunningTransitions;
    public bool HasRunningScaleTransitions => _scaleController.HasRunningTransitions;
    private readonly WidthTransitionController _widthController = new();
    private readonly ScaleTransitionController _scaleController = new();
    private readonly Dictionary<Element, WidthTransitionStyle> _widthBefore = new();
    private readonly Dictionary<Element, ScaleTransitionStyle> _scaleBefore = new();
    private readonly Dictionary<Element, double> _previousResolvedWidths = new();
    private readonly HashSet<Element> _geometrySeen = new();
    private readonly List<Element> _geometryRemoved = new();

    private void ReconcileGeometryStyles(double now)
    {
        _geometrySeen.Clear();
        ObserveGeometry(Document, now);
        foreach (var element in _widthBefore.Keys)
            if (!_geometrySeen.Contains(element)) _geometryRemoved.Add(element);
        foreach (var element in _geometryRemoved)
        {
            _widthController.Remove(element, now); _scaleController.Remove(element, now);
            _widthBefore.Remove(element); _scaleBefore.Remove(element);
        }
        _geometryRemoved.Clear();
    }
    private void ObserveGeometry(Node node, double now)
    {
        if (node is Element element)
        {
            // Preserve typed percentages; CSSOM's resolved width must not become an animation target.
            var style = Styles.GetComputedValues(element);
            if (style.GetPropertyValue("display") == "none") return;
            var width = WidthTransitionStyle.Resolve(style);
            var scale = ScaleTransitionStyle.Resolve(style);
            _geometrySeen.Add(element);
            bool widthBound = _widthController.TryGetWidth(element, out _);
            if (widthBound || width.Enabled && (width.Duration > 0 || width.Delay > 0))
            {
                WidthTransitionStyle? before = !widthBound && _widthBefore.TryGetValue(element, out var prior) ? prior : null;
                double? parentWidth = element.ParentElement is { } parent && _previousResolvedWidths.TryGetValue(parent, out double resolved)
                    ? resolved : null;
                _widthController.UpdateStyle(element, style, now, parentWidth, before);
            }
            bool scaleBound = _scaleController.Contains(element);
            if (scaleBound || scale.Enabled && (scale.Duration > 0 || scale.Delay > 0))
            {
                ScaleTransitionStyle? before = !scaleBound && _scaleBefore.TryGetValue(element, out var prior) ? prior : null;
                _scaleController.UpdateStyle(element, style, now, before);
            }
            _widthBefore[element] = width; _scaleBefore[element] = scale;
        }
        for (var child = node.FirstChild; child != null; child = child.NextSibling) ObserveGeometry(child, now);
    }
    private void CaptureGeometryWidths()
    {
        _previousResolvedWidths.Clear();
        foreach (var box in DisplayList.Boxes) _previousResolvedWidths[box.Element] = box.Bounds.Width;
    }
    private void ClearDocumentGeometry(double now)
    {
        _widthController.Clear(now); _scaleController.Clear(now);
        _widthBefore.Clear(); _scaleBefore.Clear(); _previousResolvedWidths.Clear();
        _geometrySeen.Clear(); _geometryRemoved.Clear();
    }
    private void DispatchGeometryEvents()
    { _widthController.DispatchEvents(); _scaleController.DispatchEvents(); }
}
