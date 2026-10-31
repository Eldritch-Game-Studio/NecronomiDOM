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

    private CanvasItem CompositionParent()
    {
        if (!LinearComposition) return this;
        if (_linearDocument == null)
        {
            _sceneCopy = new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport };
            AddChild(_sceneCopy);
            _linearDocument = new LinearDocumentComposite();
            AddChild(_linearDocument);
        }
        _linearDocument.BeginRebuild();
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
