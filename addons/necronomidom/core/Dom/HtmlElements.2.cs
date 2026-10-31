namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4 img element interface identity.</summary>
    public partial class HTMLImageElement : HTMLElement
    {
        internal HTMLImageElement(Document document) : base(document, "img") { }
    }
    /// <summary>HTML § 4 picture element interface identity.</summary>
    public partial class HTMLPictureElement : HTMLElement
    {
        internal HTMLPictureElement(Document document) : base(document, "picture") { }
    }
    /// <summary>HTML § 4 source element interface identity.</summary>
    public partial class HTMLSourceElement : HTMLElement
    {
        internal HTMLSourceElement(Document document) : base(document, "source") { }
    }
    /// <summary>HTML § 4 canvas element interface identity.</summary>
    public partial class HTMLCanvasElement : HTMLElement
    {
        internal HTMLCanvasElement(Document document) : base(document, "canvas") { }
    }
    /// <summary>HTML § 4 a element interface identity.</summary>
    public partial class HTMLAnchorElement : HTMLElement
    {
        internal HTMLAnchorElement(Document document) : base(document, "a") { }
    }
    /// <summary>HTML § 4 label element interface identity.</summary>
    public partial class HTMLLabelElement : HTMLElement
    {
        internal HTMLLabelElement(Document document) : base(document, "label") { }
    }
    /// <summary>HTML § 4 optgroup element interface identity.</summary>
    public partial class HTMLOptGroupElement : HTMLElement
    {
        internal HTMLOptGroupElement(Document document) : base(document, "optgroup") { }
    }
    /// <summary>HTML § 4 selectedcontent element interface identity.</summary>
    public partial class HTMLSelectedContentElement : HTMLElement
    {
        internal HTMLSelectedContentElement(Document document) : base(document, "selectedcontent") { }
    }
    /// <summary>HTML § 4 fieldset element interface identity.</summary>
    public partial class HTMLFieldSetElement : HTMLElement
    {
        internal HTMLFieldSetElement(Document document) : base(document, "fieldset") { }
    }
    /// <summary>HTML § 4 legend element interface identity.</summary>
    public partial class HTMLLegendElement : HTMLElement
    {
        internal HTMLLegendElement(Document document) : base(document, "legend") { }
    }
    /// <summary>HTML § 4 progress element interface identity.</summary>
    public partial class HTMLProgressElement : HTMLElement
    {
        internal HTMLProgressElement(Document document) : base(document, "progress") { }
    }
    /// <summary>HTML § 4 meter element interface identity.</summary>
    public partial class HTMLMeterElement : HTMLElement
    {
        internal HTMLMeterElement(Document document) : base(document, "meter") { }
    }
    /// <summary>HTML § 4 output element interface identity.</summary>
    public partial class HTMLOutputElement : HTMLElement
    {
        internal HTMLOutputElement(Document document) : base(document, "output") { }
    }
    /// <summary>HTML § 4 details element interface identity.</summary>
    public partial class HTMLDetailsElement : HTMLElement
    {
        internal HTMLDetailsElement(Document document) : base(document, "details") { }
    }
    /// <summary>HTML § 4 dialog element interface identity.</summary>
    public partial class HTMLDialogElement : HTMLElement
    {
        internal HTMLDialogElement(Document document) : base(document, "dialog") { }
    }
    /// <summary>HTML § 4 form element interface identity.</summary>
    public partial class HTMLFormElement : HTMLElement
    {
        internal HTMLFormElement(Document document) : base(document, "form") { }
    }
    /// <summary>HTML § 4 button element interface identity.</summary>
    public partial class HTMLButtonElement : HTMLElement
    {
        internal HTMLButtonElement(Document document) : base(document, "button") { }
        /// <summary>HTML § 4.10.6 name content-attribute reflection.</summary>
        public string Name { get => GetAttribute("name") ?? ""; set => SetAttribute("name", value); }
    }
}
