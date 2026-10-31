using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM 1 § 6 mutable map; warmed numeric and same-shape transform writes reuse declaration-owned slots.</summary>
    public sealed partial class StylePropertyMap : StylePropertyMapReadOnly
    {
        private readonly Dictionary<(string, string), CssValue> _keywords = new();
        internal StylePropertyMap(Func<CSSStyleDeclaration> declaration) : base(declaration) { }
        public void Set(string property, CSSUnitValue value)
        {
            string name = PropertyName(property);
            if (!double.IsFinite(value.Value)) { Set(name, CSSStyleValue.Parse(name, value.ToString())); return; }
            var declaration = Declaration;
            CssValue? old = Find(declaration, name);
            if (old is TypedNumberSlot numberOld && numberOld.Current.Unit == value.Unit && numberOld.Current.Value == value.Value
                || old is TypedPercentageSlot percentOld && percentOld.Current.Unit == value.Unit && percentOld.Current.Value == value.Value
                || old is TypedDimensionSlot dimensionOld && dimensionOld.Current.Unit == value.Unit && dimensionOld.Current.Value == value.Value) return;
            if (!NumericPropertyGrammar.Accepts(name, value)) throw new DomTypeError("The numeric type does not match the property's grammar.");
            CssValue slot;
            declaration.BeforeChange?.Invoke();
            if (value.Unit == "number")
            {
                var number = old as TypedNumberSlot ?? new TypedNumberSlot(value); number.Current = value; slot = number;
            }
            else if (value.Unit == "percent")
            {
                var percentage = old as TypedPercentageSlot ?? new TypedPercentageSlot(value); percentage.Current = value; slot = percentage;
            }
            else
            {
                var dimension = old as TypedDimensionSlot ?? new TypedDimensionSlot(value); dimension.Current = value; slot = dimension;
            }
            declaration.SetTypedProperty(name, slot);
        }
        public void Set(string property, CSSKeywordValue value)
        {
            string name = PropertyName(property);
            if (!_keywords.TryGetValue((name, value.Value), out var parsed))
            {
                var style = CSSStyleValue.Parse(name, value.ToString());
                if (CssPropertyRegistry.Get(name)?.IsShorthand == true) { Set(name, style); return; }
                parsed = style.Value;
                _keywords[(name, value.Value)] = parsed;
            }
            Declaration.SetTypedProperty(name, parsed);
        }
        public void Set(string property, CSSNumericValue value)
        {
            if (value.TryGetUnit(out var unit)) { Set(property, unit); return; }
            string name = PropertyName(property);
            if (!NumericPropertyGrammar.Accepts(name, value)) throw new DomTypeError("The numeric type does not match the property's grammar.");
            var declaration = Declaration;
            var old = Find(declaration, name) as TypedMathSlot;
            if (old != null && ReferenceEquals(old.Current.Expression, value.Expression)) return;
            declaration.BeforeChange?.Invoke();
            var slot = old ?? new TypedMathSlot(value);
            slot.Current = value;
            declaration.SetTypedProperty(name, slot);
        }
        public void Set(string property, CSSUnparsedValue value) => Set(property, CSSStyleValue.Parse(property, value.ToString()));
        public void Set(string property, string value) => Set(property, CSSStyleValue.Parse(property, value));
        public void Set(string property, CSSStyleValue value)
        {
            if (value.TryGetUnit(out var unit)) { Set(property, unit); return; }
            if (value.TryGetTransform(out var transform)) { Set(property, transform); return; }
            if (value.TryGetNumeric(out var numeric)) { Set(property, numeric); return; }
            string name = PropertyName(property);
            if (value.Property != null && value.Property != name) throw new DomTypeError("The unsupported style value is associated with another property.");
            if (value.Property == name && CssPropertyRegistry.Get(name)?.IsShorthand != true)
            { Declaration.SetTypedProperty(name, value.Value); return; }
            var parsed = CssPropertyParser.Parse(name, CssParser.ParseComponentValueList(value.ToString()))
                ?? throw new DomTypeError("The style value cannot be assigned to this property.");
            var declaration = Declaration;
            foreach (var item in parsed.Longhands) declaration.SetTypedProperty(item.Longhand, item.Value);
        }
        public void Append(string property, CSSStyleValue value)
        {
            string name = PropertyName(property);
            var existing = Find(Declaration, name);
            var definition = CssPropertyRegistry.Get(name);
            if (definition == null || definition.IsShorthand || !definition.Syntax.Contains('#'))
                throw new DomTypeError("This property does not accept appended list values.");
            Set(name, (existing == null ? "" : existing.Serialize() + ", ") + value.ToString());
        }
        public void Delete(string property) => Declaration.RemoveTypedProperty(PropertyName(property));
        public void Clear() => Declaration.ClearTypedProperties();
    }
}
