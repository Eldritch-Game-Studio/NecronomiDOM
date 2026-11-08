using System.Collections.Concurrent;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    /// <summary>Bounded memo of <see cref="CSSStyleDeclaration.SetProperty"/> parses. Parsing a property's value text is
    /// context-free and produces immutable values, so equal (property, text) pairs reuse one result; a rejected value
    /// is remembered as null. Scripts and hosts that rewrite inline styles every frame (positions, transforms,
    /// opacity) then stop tokenizing and grammar-matching the same text, and unchanged writes allocate nothing.</summary>
    internal static class ParsedValueCache
    {
        private const int Capacity = 1024;
        private static readonly ConcurrentDictionary<(string Property, string Value), ParsedProperty?> Entries = new();

        internal static ParsedProperty? Parse(string property, string value)
        {
            if (Entries.TryGetValue((property, value), out ParsedProperty? cached)) return cached;
            RawDeclaration? declaration = CssParser.ParseDeclaration(CssSerialization.SerializeIdentifier(property) + ":" + value);
            ParsedProperty? parsed = declaration == null || declaration.Important ? null : CssPropertyParser.Parse(declaration.Name, declaration.Value);
            // Values that change every frame never repeat; dropping the whole table keeps it bounded without per-entry bookkeeping.
            if (Entries.Count >= Capacity) Entries.Clear();
            Entries[(property, value)] = parsed;
            return parsed;
        }
    }
}
