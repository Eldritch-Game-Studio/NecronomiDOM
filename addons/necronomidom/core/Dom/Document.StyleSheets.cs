using System.Collections.Generic;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    public partial class Document
    {
        private Window? _defaultView;
        private IList<CSSStyleSheet>? _styleSheets;
        private IList<CSSStyleSheet>? _adoptedStyleSheets;
        /// <summary>The document's pure-C# viewing context (HTML § 7.2); no native browser is created.</summary>
        public Window DefaultView => _defaultView ??= new Window(this);
        internal void AttachDefaultView(Window view) => _defaultView = view;
        internal override EventTarget? GetTheParent(Event e) => e.Type == "load" ? null : DefaultView;
        /// <summary>Imported and presenter-provided document sheets in source order (CSSOM § 6.2).</summary>
        public IList<CSSStyleSheet> StyleSheets => _styleSheets ??= new DocumentStyleSheetList(this);
        /// <summary>Constructed sheets adopted by this document (CSSOM § 6.2.3).</summary>
        public IList<CSSStyleSheet> AdoptedStyleSheets => _adoptedStyleSheets ??= new StyleSheetCollection(this);
    }
}
