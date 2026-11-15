#nullable enable
using Godot;
using NecronomiDom.Paint;

namespace NecronomiDom.Runtime;

public partial class DocumentPaint
{
    /// <summary>Opt-in linear-light compositing; ordinary CSS retains sRGB compositing.</summary>
    public bool LinearComposition { get; internal set; }
    private LinearDocumentComposite? _linearDocument;
    private BackBufferCopy? _sceneCopy;

    /// <summary>Constructs the linear composite on the first rebuild, even one that paints nothing: a SubViewport
    /// constructor waits for the render thread, so a document mounted empty (or hidden) pays it when mounted, not on
    /// the frame it first reveals content. Until then it is a minimum-size, invisible target.</summary>
    private void EnsureLinearDocument()
    {
        if (!LinearComposition || _linearDocument != null) return;
        _sceneCopy = new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport, Visible = false };
        AddChild(_sceneCopy);
        _linearDocument = new LinearDocumentComposite { Visible = false };
        AddChild(_linearDocument);
    }

    private CanvasItem CompositionParent()
    {
        if (!LinearComposition) return this;
        EnsureLinearDocument();
        _linearDocument!.BeginRebuild();
        _isolatedContexts.Add(_linearDocument);
        _sceneCopy!.Visible = true;
        return _linearDocument.Content;
    }

    private Color Color(Rgba c)
    {
        var color = new Color(c.R, c.G, c.B, c.A);
        return LinearComposition ? color.SrgbToLinear() : color;
    }
}
