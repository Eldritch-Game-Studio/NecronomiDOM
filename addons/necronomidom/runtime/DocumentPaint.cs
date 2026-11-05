#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>One child canvas item per consecutive run; box instances have unmodulated style-row data.</summary>
[GodotClass]
public partial class DocumentPaint : Node2D
{
    private readonly List<Node2D> _items = new();
    private readonly List<Resource> _resources = new();
    private readonly PolygonPaintStore _polygons = new();
    private readonly NativeGlyphPaintStore _glyphs = new();
    private BorderPaintStore? _borders;
    private EllipticalClipStore? _ellipticalClips;
    private NativeBackgroundPaintStore? _backgrounds;
    private NativeGradientPaintStore? _gradients;
    private Image? _stylePixels;
    private ImageTexture? _styleTexture;
    private int _styleRowCount;
    private readonly List<PolygonComposite> _isolatedContexts = new();
    private readonly List<(CanvasItem Parent, PaintRun Begin)> _contexts = new();
    private readonly List<PolygonOpacityGroup> _opacityGroups = new();
    public int BoxInstanceCount { get; private set; }
    public int TextRunCount { get; private set; }
    public int ImageRunCount { get; private set; }
    public int BackgroundImageRunCount { get; private set; }
    public int GradientRunCount { get; private set; }
    public int PolygonRunCount { get; private set; }
    public int BorderBoxRunCount { get; private set; }
    public int PaintContextCount { get; private set; }
    public int RebuildRevision { get; private set; }
    public bool RetainedUpdates { get; set; } = true;
    private void WriteBoxStyle(int row, BoxStyle style)
    {
        _stylePixels!.SetPixel(0, row, Color(style.Background));
        _stylePixels.SetPixel(1, row, Color(style.Border));
        _stylePixels.SetPixel(2, row, new Color((float)style.BorderWidth, (float)style.Radius, 0, 0));
    }
    public void Rebuild(DisplayList list, INativeFontFamilies fonts, NativeImageSource images)
    {
        if (RetainedUpdates && TryUpdateRetained(list, fonts, images)) return;
        RebuildRevision++;
        Clear();
        _polygons.BeginRebuild();
        _glyphs.BeginRebuild();
        _borders?.BeginRebuild();
        _ellipticalClips?.BeginRebuild();
        _backgrounds?.BeginRebuild();
        _gradients?.BeginRebuild();
        // Keep withdrawal and unused-slot retirement, without creating paint resources for transparent boxes.
        if (!list.RequiresRasterization) { EndStoreRebuild(); return; }
        CanvasItem parent = CompositionParent();
        // A bounded row per box permits independent animated colors/radii without mutating
        // another box that happened to share its previous style or rebuilding every glyph.
        foreach (var run in list.Runs) if (run is BoxRun) _styleRowCount++;
        PrepareRetainedBindings(list);
        _stylePixels = Image.CreateEmpty(3, Math.Max(1, _styleRowCount), false, Image.Format.Rgbaf);
        int styleRow = 0;
        foreach (var run in list.Runs) if (run is BoxRun box) WriteBoxStyle(styleRow++, box.Style);
        _styleTexture = ImageTexture.CreateFromImage(_stylePixels);
        var material = new ShaderMaterial { Shader = WebPaintShaders.Box };
        material.SetShaderParameter("styles", _styleTexture);
        _resources.Add(_stylePixels); _resources.Add(_styleTexture); _resources.Add(material);
        styleRow = 0;
        ShaderMaterial? imageMaterial = null, nearestImageMaterial = null;
        for (int i = 0; i < list.Runs.Count;)
        {
            var run = list.Runs[i];
            if (TrySinglePolygon(list, i, parent, out var singlePolygon))
            {
                var singleItem = _polygons.CreateSingle(singlePolygon);
                parent.AddChild(singleItem);
                BindRetained(i, singleItem);
                BindRetained(i + 1, singleItem);
                BindRetained(i + 2, singleItem);
                PolygonRunCount++; PaintContextCount++; i += 3; continue;
            }
            if (run is BeginRectClipRun or BeginRoundedRectClipRun or BeginPolygonClipRun or BeginOpacityRun or BeginTransformRun or BeginEllipticalClipRun)
            {
                var context = run switch
                {
                    BeginTransformRun transform => BorderStore().CreateContext(transform),
                    BeginEllipticalClipRun clip => EllipticalStore().CreateContext(clip),
                    _ => _polygons.CreateContext(run)
                };
                parent.AddChild(context); _contexts.Add((parent, run));
                BindRetained(i, context);
                if (context is PolygonOpacityGroup opacity) _opacityGroups.Add(opacity);
                if (context is PolygonComposite composite)
                {
                    _isolatedContexts.Add(composite);
                    parent = composite.Content;
                }
                else parent = context;
                PaintContextCount++; i++; continue;
            }
            if (run is EndRectClipRun or EndRoundedRectClipRun or EndPolygonClipRun or EndOpacityRun or EndTransformRun or EndEllipticalClipRun)
            {
                if (_contexts.Count == 0 || !MatchingContext(_contexts[^1].Begin, run))
                    throw new InvalidOperationException("Unbalanced native paint context: " + run.GetType().Name);
                parent = _contexts[^1].Parent; _contexts.RemoveAt(_contexts.Count - 1); i++; continue;
            }
            if (run is GradientRun gradient)
            {
                var context = GradientStore().Create(gradient);
                parent.AddChild(context); _isolatedContexts.Add(context);
                BindRetained(i, context);
                GradientRunCount++; i++; continue;
            }
            if (run is BackgroundImageRun background)
            {
                var context = BackgroundStore().Create(background, images, out int backgroundSlot);
                parent.AddChild(context); _isolatedContexts.Add(context);
                BindRetained(i, context, backgroundSlot);
                BackgroundImageRunCount++; ImageRunCount++; i++; continue;
            }
            if (run is BorderBoxRun border)
            {
                var borderItem = BorderStore().Create(border, out int borderSlot);
                parent.AddChild(borderItem); BindRetained(i, borderItem, borderSlot);
                BorderBoxRunCount++; BoxInstanceCount++; i++; continue;
            }
            if (run is PolygonRun polygon)
            {
                var polygonItem = _polygons.Create(polygon);
                parent.AddChild(polygonItem); BindRetained(i, polygonItem);
                PolygonRunCount++; i++; continue;
            }
            if (run is TextEffectsRun effects)
            {
                var item = _glyphs.Create(effects, fonts, out int glyphSlot);
                parent.AddChild(item); BindRetained(i, item, glyphSlot); TextRunCount++; i++; continue;
            }
            if (list.Runs[i] is TextRun text)
            {
                var item = new ShapedTextPaint(fonts.Resolve(text), text); parent.AddChild(item); _items.Add(item);
                BindRetained(i, item, _items.Count - 1); TextRunCount++; i++; continue;
            }
            if (list.Runs[i] is ImageRun imageRun)
            {
                var source = images.Texture(imageRun);
                bool nearest = imageRun.Sampling != ImageSampling.Linear;
                var currentImageMaterial = nearest ? nearestImageMaterial : imageMaterial;
                if (currentImageMaterial == null)
                {
                    currentImageMaterial = new ShaderMaterial { Shader = WebPaintShaders.Image };
                    currentImageMaterial.SetShaderParameter("nearest_sampling", nearest);
                    _resources.Add(currentImageMaterial);
                    if (nearest) nearestImageMaterial = currentImageMaterial; else imageMaterial = currentImageMaterial;
                }
                var item = new NativeImagePaint { Run = imageRun, ImageTexture = source.Texture, Source = source.Source,
                    RasterDestination = NecronomiDom.Layout.ImageLayout.RasterBounds(imageRun.Destination),
                    Material = currentImageMaterial, TextureRepeat = TextureRepeatEnum.Disabled,
                    TextureFilter = nearest ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear };
                item.ConfigureCoordinates();
                parent.AddChild(item); _items.Add(item); BindRetained(i, item); ImageRunCount++; i++; continue;
            }
            if (run is not BoxRun)
                throw new NotSupportedException("Unsupported native paint run: " + run.GetType().FullName);
            int end = i;
            while (end < list.Runs.Count && list.Runs[end] is BoxRun) end++;
            var mesh = new QuadMesh { Size = Vector2.One };
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseCustomData = true, Mesh = mesh, InstanceCount = end - i };
            Rect2 bounds = default;
            int firstStyleRow = styleRow;
            for (int j = i; j < end; j++)
            {
                var box = (BoxRun)list.Runs[j]; var b = box.Bounds;
                var rect = new Rect2((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                multi.SetInstanceTransform2D(j - i, new Transform2D(new Vector2((float)b.Width, 0), new Vector2(0, (float)b.Height), rect.GetCenter()));
                multi.SetInstanceCustomData(j - i, BoxCustomData(styleRow++, b.Width, b.Height));
                bounds = j == i ? rect : bounds.Merge(rect); BoxInstanceCount++;
            }
            multi.CustomAabb = new Aabb(new Vector3(bounds.Position.X, bounds.Position.Y, 0), new Vector3(bounds.Size.X, bounds.Size.Y, 0));
            var item2 = new MultiMeshInstance2D { Multimesh = multi, Material = material };
            parent.AddChild(item2); _items.Add(item2); _resources.Add(multi); _resources.Add(mesh);
            for (int j = i; j < end; j++) BindRetained(j, item2, j - i, firstStyleRow + j - i);
            i = end;
        }
        if (_contexts.Count != 0) throw new InvalidOperationException("Unclosed native paint context.");
        EndStoreRebuild();
        // Stores have independent slot order. Fit the actual combined subtree only after
        // every live context is measured, so each child sees its final parent canvas once.
        foreach (var context in _isolatedContexts) context.MeasurePaint();
        foreach (var context in _isolatedContexts) context.FitTarget(true);
    }
    private void EndStoreRebuild()
    {
        _polygons.EndRebuild();
        _glyphs.EndRebuild();
        _borders?.EndRebuild();
        _ellipticalClips?.EndRebuild();
        _backgrounds?.EndRebuild();
        _gradients?.EndRebuild();
    }
    // x=style row, y=empty clip chain, zw=local box dimensions. Never use modulate for indices.
    private static Color BoxCustomData(int styleRow, double width, double height) => new(styleRow, 0, (float)width, (float)height);
    private static bool MatchingContext(PaintRun begin, PaintRun end) => (begin, end) is
        (BeginRectClipRun, EndRectClipRun) or (BeginRoundedRectClipRun, EndRoundedRectClipRun) or
        (BeginPolygonClipRun, EndPolygonClipRun) or (BeginOpacityRun, EndOpacityRun) or
        (BeginTransformRun, EndTransformRun) or (BeginEllipticalClipRun, EndEllipticalClipRun);
    private BorderPaintStore BorderStore()
    {
        if (_borders == null) { _borders = new BorderPaintStore(); _borders.BeginRebuild(); }
        return _borders;
    }
    private EllipticalClipStore EllipticalStore()
    {
        if (_ellipticalClips == null) { _ellipticalClips = new EllipticalClipStore(); _ellipticalClips.BeginRebuild(); }
        return _ellipticalClips;
    }
    private NativeBackgroundPaintStore BackgroundStore()
    {
        if (_backgrounds == null) { _backgrounds = new NativeBackgroundPaintStore(); _backgrounds.BeginRebuild(); }
        return _backgrounds;
    }
    private NativeGradientPaintStore GradientStore()
    {
        if (_gradients == null) { _gradients = new NativeGradientPaintStore { LinearComposition = LinearComposition }; _gradients.BeginRebuild(); }
        return _gradients;
    }
    /// <summary>Samples the retained whole-group alpha without rebuilding layout, glyphs, meshes or child alpha.</summary>
    public void UpdateOpacity(int groupOrdinal, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha < 0 || alpha > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha));
        if ((uint)groupOrdinal >= (uint)_opacityGroups.Count)
            throw new ArgumentOutOfRangeException(nameof(groupOrdinal));
        _opacityGroups[groupOrdinal].Update(alpha, true);
    }
    public void Clear()
    {
        _contexts.Clear();
        _retainedBindings.Clear();
        _dirtyTargets.Clear();
        _isolatedContexts.Clear();
        _opacityGroups.Clear();
        _glyphs.DetachBeforeClear();
        _polygons.DetachBeforeClear();
        _borders?.DetachBeforeClear();
        _ellipticalClips?.DetachBeforeClear();
        _backgrounds?.DetachBeforeClear();
        _gradients?.DetachBeforeClear();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i]; item.GetParent()?.RemoveChild(item); item.Free();
        }
        _items.Clear();
        foreach (var resource in _resources) resource.Dispose();
        _resources.Clear(); BoxInstanceCount = 0; TextRunCount = 0; ImageRunCount = 0;
        _stylePixels = null; _styleTexture = null; _styleRowCount = 0;
        PolygonRunCount = 0; BorderBoxRunCount = 0; PaintContextCount = 0;
        BackgroundImageRunCount = 0;
        if (_linearDocument != null) _linearDocument.Visible = false;
        if (_sceneCopy != null) _sceneCopy.Visible = false;
        GradientRunCount = 0;
    }
    /// <summary>Retire all image borrowers before their document-scoped texture owners are disposed.
    /// Ordinary paint rebuilds keep the detached background slots for reuse within the same document.</summary>
    public void WithdrawDocument()
    {
        Clear();
        _backgrounds?.Dispose(); _backgrounds = null;
        _glyphs.Dispose();
    }
    public override void _ExitTree() => Clear();
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        Clear(); _polygons.Dispose(); _borders?.Dispose(); _ellipticalClips?.Dispose(); _backgrounds?.Dispose();
        _gradients?.Dispose();
        _glyphs.Dispose();
    }
}

