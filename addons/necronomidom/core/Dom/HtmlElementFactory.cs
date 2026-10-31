namespace NecronomiDom.Dom
{
    /// <summary>HTML § 4 element interfaces for the NecronomiDOM markup profile; foreign elements use Element.</summary>
    public static class HtmlElementFactory
    {
        public static HTMLElement Create(Document document, string name)
        {
            if (document.CustomElements.Create(name) is { } custom) return custom;
            return name switch
            {
                "html" => new HTMLHtmlElement(document), "head" => new HTMLHeadElement(document),
                "body" => new HTMLBodyElement(document), "title" => new HTMLTitleElement(document),
                "meta" => new HTMLMetaElement(document), "link" => new HTMLLinkElement(document),
                "style" => new HTMLStyleElement(document), "script" => new HTMLScriptElement(document),
                "template" => new HTMLTemplateElement(document), "slot" => new HTMLSlotElement(document),
                "div" => new HTMLDivElement(document), "span" => new HTMLSpanElement(document),
                "p" => new HTMLParagraphElement(document), "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => new HTMLHeadingElement(document, name),
                "ul" => new HTMLUListElement(document), "ol" => new HTMLOListElement(document),
                "li" => new HTMLLIElement(document), "dl" => new HTMLDListElement(document),
                "br" => new HTMLBRElement(document), "hr" => new HTMLHRElement(document),
                "time" => new HTMLTimeElement(document), "q" or "blockquote" => new HTMLQuoteElement(document, name),
                "img" => new HTMLImageElement(document), "picture" => new HTMLPictureElement(document),
                "source" => new HTMLSourceElement(document), "canvas" => new HTMLCanvasElement(document),
                "button" => new HTMLButtonElement(document), "a" => new HTMLAnchorElement(document),
                "label" => new HTMLLabelElement(document), "input" => new HTMLInputElement(document),
                "select" => new HTMLSelectElement(document), "option" => new HTMLOptionElement(document),
                "optgroup" => new HTMLOptGroupElement(document), "selectedcontent" => new HTMLSelectedContentElement(document),
                "textarea" => new HTMLTextAreaElement(document), "fieldset" => new HTMLFieldSetElement(document),
                "legend" => new HTMLLegendElement(document), "progress" => new HTMLProgressElement(document),
                "meter" => new HTMLMeterElement(document), "output" => new HTMLOutputElement(document),
                "details" => new HTMLDetailsElement(document), "dialog" => new HTMLDialogElement(document),
                "form" => new HTMLFormElement(document),
                "section" or "header" or "footer" or "nav" or "main" or "aside" or "article" or "dt" or "dd" or "wbr" or "b" or "i" or "u" or "s" or "strong" or "em" or "small" or "mark" or "kbd" or "code" or "sub" or "sup" or "abbr" or "bdi" or "bdo" or "summary" or "noscript" => new HTMLElement(document, name),
                _ => CustomElementRegistry.IsCustomName(name) ? new HTMLElement(document, name) : new HTMLUnknownElement(document, name)
            };
        }
    }
    /// <summary>HTML § 4.12.3 template contents with an inert owner document.</summary>
    public sealed class HTMLTemplateElement : HTMLElement
    {
        internal HTMLTemplateElement(Document document) : base(document, "template")
        { Content = new DocumentFragment(document.TemplateDocument) { HostInternal = this }; }
        public DocumentFragment Content { get; }
    }
}
