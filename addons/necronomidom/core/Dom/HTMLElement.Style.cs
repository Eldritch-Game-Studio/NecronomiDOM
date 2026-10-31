using System;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    public partial class HTMLElement
    {
        private CSSStyleDeclaration? _style;
        private bool _reflectingStyle;
        private bool _styleChangePending;
        private string? _styleOldValue;
        private string? _serializedStyle;
        private Func<string>? _styleProvider;
        /// <summary>CSSOM § 6.6 inline declaration block, live with the HTML style attribute.</summary>
        public CSSStyleDeclaration Style
        {
            get
            {
                if (_style != null) return _style;
                _style = CSSStyleDeclaration.Parse(GetAttribute("style") ?? "");
                _style.Changed = ReflectStyle;
                _style.BeforeChange = BeginStyleChange;
                _styleProvider = SerializeStyle;
                return _style;
            }
        }
        private string SerializeStyle() => _serializedStyle ??= _style!.CssText;
        private void BeginStyleChange()
        {
            if (_reflectingStyle || _styleChangePending) return;
            _styleChangePending = true;
            _styleOldValue = StyleOldValueRequired() ? GetAttribute("style") : null;
        }
        private void ReflectStyle()
        {
            if (_reflectingStyle || _style == null) return;
            _reflectingStyle = true;
            _serializedStyle = null;
            try { ReflectStyleAttribute(_styleProvider!, _styleOldValue); }
            finally { _reflectingStyle = false; _styleChangePending = false; _styleOldValue = null; }
        }
        protected internal override void AttributeChanged(string localName, string? ns, string? oldValue, string? newValue)
        {
            base.AttributeChanged(localName, ns, oldValue, newValue);
            if (ns != null || localName != "style" || _style == null || _reflectingStyle) return;
            _reflectingStyle = true;
            _serializedStyle = null;
            try { _style.CssText = newValue ?? ""; }
            finally { _reflectingStyle = false; }
        }
    }
}
