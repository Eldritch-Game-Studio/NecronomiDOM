using System;
using System.Collections;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>CSS Typed OM 1 § 6 read-only map over a live declaration block.</summary>
    public class StylePropertyMapReadOnly : IEnumerable<KeyValuePair<string, IReadOnlyList<CSSStyleValue>>>
    {
        private readonly Func<CSSStyleDeclaration> _declaration;
        internal StylePropertyMapReadOnly(Func<CSSStyleDeclaration> declaration) => _declaration = declaration;
        protected CSSStyleDeclaration Declaration { get { var declaration = _declaration(); _ = declaration.Length; return declaration; } }
        public int Size => Declaration.Length;
        internal static string PropertyName(string name)
        {
            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                if (!CssTypeParser.TryParse("dashed-ident", CssParser.ParseComponentValueList(name), out _)) throw new DomTypeError("Invalid custom property name.");
                return name;
            }
            return CssPropertyRegistry.Get(name)?.Name ?? throw new DomTypeError("Unknown CSS property.");
        }
        public CSSStyleValue? Get(string property)
        {
            string name = PropertyName(property);
            var declaration = Declaration;
            CssValue? value = Find(declaration, name);
            if (value == null && CssPropertyRegistry.Get(name)?.IsShorthand == true)
            {
                string shorthand = declaration.GetPropertyValue(name);
                return shorthand.Length == 0 ? null : CSSStyleValue.Parse(name, shorthand);
            }
            if (name != "mask-position" && value is CssListValue { Separator: CssListSeparator.Comma } list && value is not TypedTransformSlot)
                value = list.Items.Count == 0 ? null : list.Items[0];
            return value == null ? null : new CSSStyleValue(value, name);
        }
        public IReadOnlyList<CSSStyleValue> GetAll(string property)
        {
            string name = PropertyName(property);
            var value = Find(Declaration, name);
            if (value == null) { var shorthand = Get(name); return shorthand.HasValue ? new[] { shorthand.Value } : Array.Empty<CSSStyleValue>(); }
            if (name != "mask-position" && value is CssListValue { Separator: CssListSeparator.Comma } list && value is not TypedTransformSlot)
            {
                var result = new CSSStyleValue[list.Items.Count];
                for (int i = 0; i < result.Length; i++) result[i] = new CSSStyleValue(list.Items[i], name);
                return result;
            }
            return new[] { new CSSStyleValue(value, name) };
        }
        public bool Has(string property) => Get(property).HasValue;
        internal static CssValue? Find(CSSStyleDeclaration declaration, string name)
        {
            for (int i = 0; i < declaration.Declarations.Count; i++)
                if (declaration.Declarations[i].Name == name) return declaration.Declarations[i].Value;
            return null;
        }
        public IEnumerator<KeyValuePair<string, IReadOnlyList<CSSStyleValue>>> GetEnumerator()
        {
            var declaration = Declaration;
            for (int i = 0; i < declaration.Declarations.Count; i++)
            {
                string name = declaration.Declarations[i].Name;
                yield return new KeyValuePair<string, IReadOnlyList<CSSStyleValue>>(name, GetAll(name));
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
