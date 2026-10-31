#nullable enable
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>Background-color transition interpolation profile chosen before binding; ordinary documents remain CSS.</summary>
    public BackgroundColorTransitionProfile BackgroundColorTransitionProfile
    {
        get => _backgroundColorController.Profile;
        init => _backgroundColorController = new(value);
    }
    public bool HasRunningBackgroundColorTransitions => _backgroundColorController.HasRunningTransitions;
    private readonly BackgroundColorTransitionController _backgroundColorController = new();
    private readonly Dictionary<Element, Rgba> _backgroundColorBefore = new();
    private readonly HashSet<Element> _backgroundColorSeen = new();
    private readonly List<Element> _backgroundColorRemoved = new();

    private void ReconcileBackgroundColorStyles(double now)
    {
        _backgroundColorSeen.Clear();
        ObserveBackgroundColor(Document, now);
        foreach (var element in _backgroundColorBefore.Keys)
            if (!_backgroundColorSeen.Contains(element)) _backgroundColorRemoved.Add(element);
        foreach (var element in _backgroundColorRemoved)
        { _backgroundColorController.Remove(element, now); _backgroundColorBefore.Remove(element); }
        _backgroundColorRemoved.Clear();
    }
    private void ObserveBackgroundColor(Node node, double now)
    {
        if (node is Element element)
        {
            var style = Styles.GetComputedStyle(element);
            if (style.GetPropertyValue("display") == "none") return;
            var transition = BackgroundColorTransitionStyle.Resolve(style);
            _backgroundColorSeen.Add(element);
            bool bound = _backgroundColorController.TryGetBackgroundColor(element, out _);
            if (bound || transition.Enabled && (transition.Duration > 0 || transition.Delay > 0))
            {
                Rgba? before = !bound && _backgroundColorBefore.TryGetValue(element, out Rgba prior) ? prior : null;
                _backgroundColorController.UpdateStyle(element, style, now, before);
            }
            _backgroundColorBefore[element] = transition.Color;
        }
        for (var child = node.FirstChild; child != null; child = child.NextSibling) ObserveBackgroundColor(child, now);
    }
    private void ClearDocumentBackgroundColor(double now)
    {
        _backgroundColorController.Clear(now);
        _backgroundColorBefore.Clear(); _backgroundColorSeen.Clear(); _backgroundColorRemoved.Clear();
    }
}
