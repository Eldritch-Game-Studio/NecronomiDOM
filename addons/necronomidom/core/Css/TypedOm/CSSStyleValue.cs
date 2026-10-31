using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    public enum CSSStyleValueKind { Generic, Unit, Keyword, Unparsed, Transform, Numeric }
    /// <summary>Typed OM specified/computed value view; parsing delegates to the single property grammar implementation.</summary>
    public readonly struct CSSStyleValue
    {
        private readonly CssValue? _value;
        private readonly string? _serialization;
        private readonly CSSUnitValue _unitSnapshot;
        private readonly bool _isUnit, _isTransform, _isNumeric;
        private readonly CSSTransformValue _transformSnapshot;
        private readonly CSSNumericValue _numericSnapshot;
        internal CSSStyleValue(CssValue value, string? property = null, string? serialization = null)
        {
            if (property is "object-position" or "perspective-origin")
                value = CssPositionTypedValues.Reify(value);
            _serialization = serialization;
            _value = value;
            Property = property;
            _isUnit = CSSUnitValue.TryFrom(value, out _unitSnapshot);
            _isNumeric = CSSNumericValue.TryFrom(value, out _numericSnapshot);
            _transformSnapshot = default;
            _isTransform = value is TypedTransformSlot;
            if (value is TypedTransformSlot transform) _transformSnapshot = transform.Snapshot();
            else _isTransform = Property == "transform" && TypedTransformDecoder.TryDecode(value, out _transformSnapshot);
        }
        internal CssValue Value => _value ?? throw new DomTypeError("An uninitialized CSSStyleValue cannot be assigned.");
        internal string? Property { get; }
        public CSSStyleValueKind Kind => _isTransform ? CSSStyleValueKind.Transform : Value switch
        {
            CssNumberValue or CssDimensionValue or CssPercentageValue => CSSStyleValueKind.Unit,
            CssKeywordValue or CssWideKeyword => CSSStyleValueKind.Keyword,
            CssPendingSubstitutionValue => CSSStyleValueKind.Unparsed,
            CssUnparsedValue when Property?.StartsWith("--", StringComparison.Ordinal) == true => CSSStyleValueKind.Unparsed,
            TypedTransformSlot => CSSStyleValueKind.Transform,
            Values.Math.CssMathValue => CSSStyleValueKind.Numeric,
            _ => CSSStyleValueKind.Generic,
        };
        public bool TryGetUnit(out CSSUnitValue value) { value = _unitSnapshot; return _isUnit; }
        public bool TryGetTransform(out CSSTransformValue value) { value = _transformSnapshot; return _isTransform; }
        public bool TryGetNumeric(out CSSNumericValue value) { value = _numericSnapshot; return _isNumeric; }
        public bool TryGetUnparsed(out CSSUnparsedValue? value)
        {
            IReadOnlyList<ComponentValue>? tokens = Value switch
            {
                CssUnparsedValue raw => raw.Tokens,
                CssPendingSubstitutionValue pending => pending.Tokens,
                _ => null,
            };
            value = tokens == null ? null : CSSUnparsedValue.FromTokens(tokens);
            return value != null;
        }
        public bool TryGetKeyword(out CSSKeywordValue value)
        {
            string? keyword = Value is CssKeywordValue k ? k.Value : Value is CssWideKeyword wide ? wide.Serialize() : null;
            value = keyword == null ? default : new CSSKeywordValue(keyword);
            return keyword != null;
        }
        public static CSSStyleValue Parse(string property, string text)
        {
            var tokens = CssParser.ParseComponentValueList(text);
            var parsed = CssPropertyParser.Parse(property, tokens) ?? throw new DomTypeError("Invalid CSS property value.");
            if (parsed.Longhands.Count == 1)
                return new CSSStyleValue(parsed.Longhands[0].Value, parsed.Longhands[0].Longhand,
                    parsed.Longhands[0].Longhand is "transform-origin" or "mask-position" ? text : null);
            if (!ShorthandSerializer.TrySerialize(property, name => Find(parsed, name), out string canonical))
                throw new DomTypeError("The shorthand cannot be represented by its declaration values.");
            return new CSSStyleValue(new CssUnparsedValue(CssParser.ParseComponentValueList(canonical)),
                CssPropertyRegistry.Get(property)?.Name ?? property, property == "background-position" ? text : null);
        }
        public static IReadOnlyList<CSSStyleValue> ParseAll(string property, string text)
        {
            CSSStyleValue value = Parse(property, text);
            // Chrome reifies unsupported mask positions as one opaque value, including layers.
            if (value.Property == "mask-position" || value.Value is not CssListValue { Separator: CssListSeparator.Comma } list) return new[] { value };
            var result = new CSSStyleValue[list.Items.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new CSSStyleValue(list.Items[i], value.Property);
            return result;
        }
        internal static CssValue? Find(ParsedProperty parsed, string name)
        { foreach (var item in parsed.Longhands) if (item.Longhand == name) return item.Value; return null; }
        public override string ToString() => _serialization ?? (_isUnit ? _unitSnapshot.ToString() : _isTransform ? _transformSnapshot.ToString() : Value.Serialize());
    }
}
