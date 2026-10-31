#nullable enable
using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentHost
{
    /// <summary>One monotonic seconds clock for style reconciliation, sampling and lifecycle cancellation.</summary>
    public Func<double> MonotonicSeconds { get; set; } = static () => Godot.Time.GetTicksUsec() * 1e-6;
    public bool HasRunningOpacityTransitions => _opacityController.HasRunningTransitions;
    private readonly OpacityTransitionController _opacityController = new();
    private readonly Dictionary<Element, double> _opacityBefore = new();
    private readonly HashSet<Element> _opacitySeen = new();
    private readonly List<Element> _opacityRemoved = new();
    private readonly List<OpacityBinding> _opacityGroups = new();
    private readonly record struct OpacityBinding(Element Element, int Ordinal, bool RetirePhaseAtOpaque);

    private void ReconcileOpacityStyles(double now)
    {
        _opacitySeen.Clear();
        ObserveOpacity(Document, now);
        foreach (var element in _opacityBefore.Keys)
            if (!_opacitySeen.Contains(element)) _opacityRemoved.Add(element);
        foreach (var element in _opacityRemoved)
        { _opacityController.Remove(element, now); _opacityBefore.Remove(element); }
        _opacityRemoved.Clear();
    }
    private void ObserveOpacity(Node node, double now)
    {
        if (node is Element element)
        {
            var style = Styles.GetComputedStyle(element);
            if (style.GetPropertyValue("display") == "none") return;
            var transition = OpacityTransitionStyle.Resolve(style);
            _opacitySeen.Add(element);
            bool bound = _opacityController.TryGetOpacity(element, out _);
            if (bound || transition.Enabled && (transition.Duration > 0 || transition.Delay > 0))
            {
                double? before = !bound && _opacityBefore.TryGetValue(element, out double prior) ? prior : null;
                _opacityController.UpdateStyle(element, style, now, before);
            }
            _opacityBefore[element] = transition.Opacity;
        }
        for (var child = node.FirstChild; child != null; child = child.NextSibling) ObserveOpacity(child, now);
    }
    private void BindOpacityGroups()
    {
        _opacityGroups.Clear();
        int ordinal = 0;
        foreach (var run in DisplayList.Runs)
        {
            if (run is not BeginOpacityRun) continue;
            if (_layout!.TryGetOpacityOwner(run, out var element))
            {
                var style = Styles.GetComputedStyle(element);
                var target = OpacityTransitionStyle.Resolve(style);
                bool retire = element.ParentElement != null && style.GetPropertyValue("position") == "static"
                    && style.GetPropertyValue("clip-path") == "none" && target.Opacity >= 1
                    && _opacityController.TryGetOpacity(element, out double opacity) && opacity < 1;
                _opacityGroups.Add(new OpacityBinding(element, ordinal, retire));
            }
            ordinal++;
        }
    }
    private bool OpacityPhaseEnded()
    {
        foreach (var binding in _opacityGroups)
            if (binding.RetirePhaseAtOpaque && _opacityController.TryGetOpacity(binding.Element, out double opacity) && opacity >= 1)
                return true;
        return false;
    }
    private void UpdateOpacityGroups()
    {
        foreach (var binding in _opacityGroups)
            if (_opacityController.TryGetOpacity(binding.Element, out double opacity))
                _paint.UpdateOpacity(binding.Ordinal, (float)opacity);
    }
    private void ClearDocumentOpacity(double now)
    {
        _opacityController.Clear(now);
        _opacityBefore.Clear(); _opacitySeen.Clear(); _opacityRemoved.Clear(); _opacityGroups.Clear();
        _inlineStyleChanges.Clear(); _inlineOpacityUpdates.Clear(); _inlineTransformUpdates.Clear();
    }
}
