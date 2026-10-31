using NecronomiDom.Css.TypedOm;

namespace NecronomiDom.Dom
{
    public partial class Element
    {
        private StylePropertyMapReadOnly? _computedStyleMap;
        /// <summary>CSS Typed OM 1 § 6.4 live computed declaration map.</summary>
        public StylePropertyMapReadOnly ComputedStyleMap()
            => _computedStyleMap ??= new StylePropertyMapReadOnly(() => NodeDocument.DefaultView.StyleEngine.GetComputedValues(this));
    }
    public partial class HTMLElement
    {
        private StylePropertyMap? _attributeStyleMap;
        /// <summary>CSS Typed OM 1 § 6.3 inline declaration map, sharing the live style block.</summary>
        public StylePropertyMap AttributeStyleMap => _attributeStyleMap ??= new StylePropertyMap(() => Style);
    }
}
