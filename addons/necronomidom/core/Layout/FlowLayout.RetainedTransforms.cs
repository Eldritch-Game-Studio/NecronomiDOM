using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly Dictionary<PaintRun, Element> _transformOwners = new(ReferenceEqualityComparer.Instance);

    /// <summary>Only an existing non-none transform carrier can change without changing containing blocks/paint phases.
    /// The caller must first admit an independent transform-only style delta through StyleEngine.</summary>
    public bool TryPrepareTransformUpdate(Element element, out int ordinal, out UsedBoxTransform transform)
    {
        ordinal = -1; transform = default;
        int current = 0;
        foreach (var run in _result.Runs)
        {
            if (run is not BeginTransformRun) continue;
            if (_transformOwners.TryGetValue(run, out var owner) && ReferenceEquals(owner, element))
            { ordinal = current; break; }
            current++;
        }
        if (ordinal < 0) return false;
        foreach (var box in _result.Boxes)
        {
            if (!ReferenceEquals(box.Element, element) || box.UsedGeometry is not { } geometry) continue;
            if (!geometry.Transform.EstablishesContainingBlock) return false;
            transform = BoxTransformResolver.Resolve(_styles.GetComputedValues(element), box.Bounds);
            return transform.EstablishesContainingBlock;
        }
        return false;
    }

    /// <summary>Commit an admitted transform and rebuild only the inverse hit/clip links, never flow or glyph paint.</summary>
    public void ApplyTransformUpdate(Element element, UsedBoxTransform transform)
    {
        for (int i = 0; i < _result.Runs.Count; i++)
        {
            var run = _result.Runs[i];
            if (run is not BeginTransformRun begin || !_transformOwners.TryGetValue(run, out var owner) ||
                !ReferenceEquals(owner, element)) continue;
            var updated = begin with { Transform = transform.Matrix };
            _result.Runs[i] = updated; _transformOwners.Remove(run); _transformOwners.Add(updated, owner);
            break;
        }
        for (int i = 0; i < _result.Boxes.Count; i++)
        {
            var box = _result.Boxes[i];
            if (ReferenceEquals(box.Element, element) && box.UsedGeometry is { } geometry)
            { _result.Boxes[i] = box with { UsedGeometry = geometry with { Transform = transform } }; break; }
        }
        CompleteHitGeometry(element);
    }
}
