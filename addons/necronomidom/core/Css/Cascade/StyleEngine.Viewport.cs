using System;
using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    // Sticky by design: once a document consumed a viewport/container unit, resize stays conservative.
    // It may miss a reuse opportunity after that declaration disappears, never retain a stale value.
    private bool _computedViewportDependent;
    private readonly Action _observeViewportUnit;
    private readonly Dictionary<(MediaQueryList Query, Node Root), bool> _observedMediaMatches = new();
    private readonly HashSet<string> _mediaMatchStack = new(StringComparer.Ordinal);
    private bool CanObserveMedia => Media.UnitScale == null && Media.ContainerStyle == null && Media.CustomMedia.Count == 0;
    private void ObserveViewportUnit() => _computedViewportDependent = true;

    /// <summary>Changes only CSS viewport dimensions, preserving all other media inputs. Cached styles remain
    /// valid when no computation consumed viewport units and every observed media-query result is unchanged.</summary>
    public bool UpdateViewport(double width, double height)
    {
        if (_media.Width == width && _media.Height == height) return false;
        EnsureSources();
        bool preserve = IncrementalReconciliation && !_computedViewportDependent && CanObserveMedia &&
            ValueContext == null && ContainerEnvironment == null && _scopeMedia.Count != 0;
        foreach (var pair in _scopeMedia)
            if (!pair.Value.SameNonViewportInputs(_media)) preserve = false;
        _media.Width = width; _media.Height = height;
        foreach (var pair in _scopeMedia) { pair.Value.Width = width; pair.Value.Height = height; }
        if (preserve)
            foreach (var pair in _observedMediaMatches)
                if (EvaluateObservedMedia(pair.Key.Query, SourceMedia(pair.Key.Root)) != pair.Value)
                { preserve = false; break; }
        if (!preserve) Invalidate();
        return true;
    }

    private bool MatchesMedia(MediaQueryList query, Node root)
    {
        var environment = SourceMedia(root);
        if (!CanObserveMedia || environment.UnitScale != null || environment.ContainerStyle != null)
        {
            _computedViewportDependent = true;
            return MediaEvaluator.Matches(query, environment);
        }
        bool matched = EvaluateObservedMedia(query, environment);
        _observedMediaMatches[(query, root)] = matched;
        return matched;
    }
    private bool EvaluateObservedMedia(MediaQueryList query, MediaEnvironment environment)
    {
        try { return MediaEvaluator.Matches(query, environment, _mediaMatchStack); }
        finally { _mediaMatchStack.Clear(); }
    }
}
