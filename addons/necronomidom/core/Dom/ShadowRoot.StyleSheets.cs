using System.Collections.Generic;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    public partial class ShadowRoot
    {
        private IList<CSSStyleSheet>? _adoptedStyleSheets;
        private IList<CSSStyleSheet>? _styleSheets;
        /// <summary>CSSOM § 6.2 connected style blocks in this tree scope.</summary>
        public IList<CSSStyleSheet> StyleSheets => _styleSheets ??= new DocumentStyleSheetList(this);
        /// <summary>Constructed sheets adopted by this shadow root (CSSOM § 6.2.3).</summary>
        public IList<CSSStyleSheet> AdoptedStyleSheets => _adoptedStyleSheets ??= new StyleSheetCollection(this);
    }
}
