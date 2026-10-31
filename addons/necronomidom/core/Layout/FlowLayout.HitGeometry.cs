using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

public sealed partial class FlowLayout
{
    private readonly Dictionary<Element, LayoutHitClip?> _descendantClips = new();
    private readonly Dictionary<Element, Affine2D> _worldTransforms = new();
    private void CompleteHitGeometry(Element? changedRoot = null)
    {
        if (changedRoot == null) { _descendantClips.Clear(); _worldTransforms.Clear(); }
        for (int i = 0; i < _result.Boxes.Count; i++)
        {
            var box = _result.Boxes[i];
            // A retained transform changes only this DOM subtree; unchanged ancestor links remain valid inputs.
            if (changedRoot != null && !changedRoot.Contains(box.Element)) continue;
            if (box.UsedGeometry is not { } geometry) continue;
            LayoutHitClip? inherited = null;
            var ancestorTransform = Affine2D.Identity;
            for (var parent = box.Element.ParentElement; parent != null; parent = parent.ParentElement)
                if (_descendantClips.TryGetValue(parent, out inherited))
                { ancestorTransform = _worldTransforms[parent]; break; }
            var worldTransform = geometry.Transform.Matrix == Affine2D.Identity
                ? ancestorTransform : Affine2D.Compose(ancestorTransform, geometry.Transform.Matrix);
            _worldTransforms[box.Element] = worldTransform;
            LayoutHitTransform? hitTransform = worldTransform == Affine2D.Identity ? null : LayoutHitTransform.From(worldTransform);
            if (geometry.Mode == PositionedMode.Absolute) inherited = inherited?.ForAbsolute(geometry.ContainingElement);
            LayoutHitClip? own = geometry.Polygon == null ? inherited : new LayoutHitClip(inherited,
                box.Element, box.Bounds, geometry.Polygon, 0, false, transform: hitTransform);
            // The element's rounded border edge limits its own hit area, not overflowing children.
            // Descendants inherit the padding-edge overflow clip separately below.
            var self = HasCorners(geometry.BorderRadii)
                ? new LayoutHitClip(own, box.Element, box.Bounds, null, 0, false, corners: geometry.BorderRadii, transform: hitTransform) : own;
            var visualBounds = hitTransform == null ? box.Bounds : worldTransform.TransformBounds(box.Bounds);
            if (!SameHitGeometry(box, self, hitTransform, visualBounds))
                _result.Boxes[i] = box with { HitClip = self, HitTransform = hitTransform, VisualBounds = visualBounds };
            var context = geometry.Context;
            _descendantClips[box.Element] = context.ClipsX || context.ClipsY
                ? new LayoutHitClip(own, box.Element, context.ClipBounds, null, context.ClipRadius, true, context.ClipsX, context.ClipsY,
                    corners: EllipticalOverflow(context) ? context.ClipRadii : null, transform: hitTransform)
                : own;
        }
    }
    internal static bool SameHitGeometry(LayoutBox box, LayoutHitClip? clip, in LayoutHitTransform? transform, in Rect visualBounds)
    {
        // The cached inverse is a value type. Keep transformed records on their original rebuild path.
        if (!ReferenceEquals(box.HitClip, clip) || box.HitTransform.HasValue || transform.HasValue) return false;
        Rect current = box.VisualBounds;
        return Same(current.X, visualBounds.X) && Same(current.Y, visualBounds.Y)
            && Same(current.Width, visualBounds.Width) && Same(current.Height, visualBounds.Height);
        static bool Same(double first, double second) =>
            BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
    }
}
