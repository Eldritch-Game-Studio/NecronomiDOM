#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using NecronomiDom.Layout;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

/// <summary>Retained background-only clipping targets; final measurement/fitting belongs to the ordered dispatcher.</summary>
public sealed class NativeBackgroundPaintStore : IDisposable
{
    private sealed class Slot(BeginEllipticalClipRun clip)
    {
        internal BeginEllipticalClipRun Clip = clip;
        internal readonly EllipticalClipGroup Group = new(clip);
        internal readonly NativeImagePaint Image = new();
    }
    private readonly List<Slot> _slots = new();
    private ShaderMaterial? _linear, _nearest;
    private int _cursor;

    public void BeginRebuild() => _cursor = 0;
    public EllipticalClipGroup Create(BackgroundImageRun run, NativeImageSource images, out int index)
    {
        if (_cursor == _slots.Count)
        {
            var added = new Slot(new BeginEllipticalClipRun(run.Clip.Bounds, run.Clip.Radii));
            added.Group.Content.AddChild(added.Image); _slots.Add(added);
        }
        index = _cursor++;
        Update(index, run, images);
        _slots[index].Group.BeginRebuild();
        return _slots[index].Group;
    }
    internal bool Owns(int index, CanvasItem? item) => (uint)index < (uint)_slots.Count
        && ReferenceEquals(_slots[index].Group, item);
    internal void Update(int index, BackgroundImageRun run, NativeImageSource images)
    {
        var slot = _slots[index];
        if (slot.Clip.Geometry != run.Clip)
        {
            slot.Clip = new BeginEllipticalClipRun(run.Clip.Bounds, run.Clip.Radii);
            slot.Group.Update(slot.Clip);
        }
        var texture = images.Texture(run.Image);
        bool nearest = run.Image.Sampling != ImageSampling.Linear;
        slot.Image.Run = run.Image;
        slot.Image.ImageTexture = texture.Texture;
        slot.Image.Source = texture.Source;
        slot.Image.RasterDestination = ImageLayout.RasterBounds(run.Image.Destination);
        slot.Image.Material = Material(nearest);
        slot.Image.TextureRepeat = CanvasItem.TextureRepeatEnum.Disabled;
        slot.Image.TextureFilter = nearest ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear;
        slot.Image.SetInstanceShaderParameter("image_edge_fringe",
            run.Clip.Radii == default && run.Image.Destination == run.Clip.Bounds);
        slot.Image.ConfigureCoordinates(); slot.Image.QueueRedraw();
    }
    private ShaderMaterial Material(bool nearest)
    {
        var material = nearest ? _nearest : _linear;
        if (material != null) return material;
        material = new ShaderMaterial { Shader = WebPaintShaders.Image };
        material.SetShaderParameter("nearest_sampling", nearest);
        if (nearest) _nearest = material; else _linear = material;
        return material;
    }
    public void DetachBeforeClear()
    {
        for (int i = _slots.Count - 1; i >= 0; i--) _slots[i].Group.GetParent()?.RemoveChild(_slots[i].Group);
    }
    public void EndRebuild()
    {
        for (int i = _slots.Count - 1; i >= _cursor; i--) _slots[i].Group.Free();
        if (_cursor < _slots.Count) _slots.RemoveRange(_cursor, _slots.Count - _cursor);
    }
    public void Dispose()
    {
        DetachBeforeClear();
        for (int i = _slots.Count - 1; i >= 0; i--) _slots[i].Group.Free();
        _slots.Clear(); _cursor = 0;
        _linear?.Dispose(); _nearest?.Dispose(); _linear = _nearest = null;
    }
}
