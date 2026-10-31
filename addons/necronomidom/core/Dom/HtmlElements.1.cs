namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4 html element interface identity.</summary>
    public partial class HTMLHtmlElement : HTMLElement
    {
        internal HTMLHtmlElement(Document document) : base(document, "html") { }
    }
    /// <summary>HTML § 4 head element interface identity.</summary>
    public partial class HTMLHeadElement : HTMLElement
    {
        internal HTMLHeadElement(Document document) : base(document, "head") { }
    }
    /// <summary>HTML § 4 body element interface identity.</summary>
    public partial class HTMLBodyElement : HTMLElement
    {
        internal HTMLBodyElement(Document document) : base(document, "body") { }
    }
    /// <summary>HTML § 4 title element interface identity.</summary>
    public partial class HTMLTitleElement : HTMLElement
    {
        internal HTMLTitleElement(Document document) : base(document, "title") { }
    }
    /// <summary>HTML § 4 meta element interface identity.</summary>
    public partial class HTMLMetaElement : HTMLElement
    {
        internal HTMLMetaElement(Document document) : base(document, "meta") { }
    }
    /// <summary>HTML § 4 link element interface identity.</summary>
    public partial class HTMLLinkElement : HTMLElement
    {
        internal HTMLLinkElement(Document document) : base(document, "link") { }
    }
    /// <summary>HTML § 4 style element interface identity.</summary>
    public partial class HTMLStyleElement : HTMLElement
    {
        internal HTMLStyleElement(Document document) : base(document, "style") { }
    }
    /// <summary>HTML § 4 script element interface identity.</summary>
    public partial class HTMLScriptElement : HTMLElement
    {
        internal HTMLScriptElement(Document document) : base(document, "script") { }
    }
    /// <summary>HTML § 4 div element interface identity.</summary>
    public partial class HTMLDivElement : HTMLElement
    {
        internal HTMLDivElement(Document document) : base(document, "div") { }
    }
    /// <summary>HTML § 4 span element interface identity.</summary>
    public partial class HTMLSpanElement : HTMLElement
    {
        internal HTMLSpanElement(Document document) : base(document, "span") { }
    }
    /// <summary>HTML § 4 p element interface identity.</summary>
    public partial class HTMLParagraphElement : HTMLElement
    {
        internal HTMLParagraphElement(Document document) : base(document, "p") { }
    }
    /// <summary>HTML § 4 ul element interface identity.</summary>
    public partial class HTMLUListElement : HTMLElement
    {
        internal HTMLUListElement(Document document) : base(document, "ul") { }
    }
    /// <summary>HTML § 4 ol element interface identity.</summary>
    public partial class HTMLOListElement : HTMLElement
    {
        internal HTMLOListElement(Document document) : base(document, "ol") { }
    }
    /// <summary>HTML § 4 li element interface identity.</summary>
    public partial class HTMLLIElement : HTMLElement
    {
        internal HTMLLIElement(Document document) : base(document, "li") { }
    }
    /// <summary>HTML § 4 dl element interface identity.</summary>
    public partial class HTMLDListElement : HTMLElement
    {
        internal HTMLDListElement(Document document) : base(document, "dl") { }
    }
    /// <summary>HTML § 4 br element interface identity.</summary>
    public partial class HTMLBRElement : HTMLElement
    {
        internal HTMLBRElement(Document document) : base(document, "br") { }
    }
    /// <summary>HTML § 4 hr element interface identity.</summary>
    public partial class HTMLHRElement : HTMLElement
    {
        internal HTMLHRElement(Document document) : base(document, "hr") { }
    }
    /// <summary>HTML § 4 time element interface identity.</summary>
    public partial class HTMLTimeElement : HTMLElement
    {
        internal HTMLTimeElement(Document document) : base(document, "time") { }
    }
    /// <summary>HTML § 4 HTMLHeadingElement element interface identity.</summary>
    public class HTMLHeadingElement : HTMLElement
    {
        internal HTMLHeadingElement(Document document, string name) : base(document, name) { }
    }
    /// <summary>HTML § 4 HTMLQuoteElement element interface identity.</summary>
    public class HTMLQuoteElement : HTMLElement
    {
        internal HTMLQuoteElement(Document document, string name) : base(document, name) { }
    }
    /// <summary>HTML § 4 HTMLUnknownElement element interface identity.</summary>
    public class HTMLUnknownElement : HTMLElement
    {
        internal HTMLUnknownElement(Document document, string name) : base(document, name) { }
    }
}
