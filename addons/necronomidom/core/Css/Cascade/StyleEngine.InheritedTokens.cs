using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Cascade;

public sealed partial class StyleEngine
{
    // Inherited unparsed syntax is retokenized once per computed value. A var-only alias can reuse
    // that same retokenized/trimmed value rather than rebuilding its wrapper and both projections.
    private sealed class InheritedCustomTokens : IReadOnlyList<ComponentValue>
    {
        private readonly IReadOnlyList<ComponentValue> _tokens;
        private CssUnparsedValue? _value;
        internal CssUnparsedValue Value => _value ??= new CssUnparsedValue(_tokens);
        internal InheritedCustomTokens(CssValue source)
        {
            _tokens = CssParser.ParseComponentValueList(source.Serialize());
        }
        public int Count => _tokens.Count;
        public ComponentValue this[int index] => _tokens[index];
        public IEnumerator<ComponentValue> GetEnumerator() => _tokens.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private readonly ConditionalWeakTable<CssValue, InheritedCustomTokens> _inheritedTokens = new();
    private IReadOnlyList<ComponentValue> InheritedTokens(CssValue inherited) => inherited is CssUnparsedValue
        ? _inheritedTokens.GetValue(inherited, static value => new InheritedCustomTokens(value))
        : CssParser.ParseComponentValueList(inherited.Serialize());
}
