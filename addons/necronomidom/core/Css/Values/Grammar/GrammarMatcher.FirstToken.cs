using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values.Grammar
{
    internal sealed partial class GrammarMatcher
    {
        // Functional-notation types resolve per scope to the same definitions; null means "not only functions".
        private static readonly ConcurrentDictionary<(TypeNode Node, string Scope), string[]?> FunctionStarts = new();

        /// <summary>A necessary condition for <see cref="Walk"/> to yield any match of a one-of alternative at
        /// <paramref name="token"/>. It mirrors the first test each node kind applies, so pruning never changes the
        /// matches or their order; it only skips alternatives that would allocate their iterators and fail, such as the
        /// twenty other transform functions tried for every <c>translate()</c>.</summary>
        private bool CanStart(GrammarNode node, ComponentValue? token) => node switch
        {
            KeywordNode keyword => token is PreservedToken k && k.Token.IsIdent(keyword.Name),
            LiteralNode literal => token is PreservedToken l && LiteralMatches(l.Token, literal.Text),
            FunctionNode function => token is FunctionValue f && NecronomiDom.Dom.Names.AsciiEqual(f.Name, function.Name),
            TypeNode type when FunctionalType(type) => FunctionStartsFor(type) is not { } names || token is FunctionValue named && StartsWith(names, named.Name),
            _ => true,
        };

        // Functional-notation types that MatchType resolves only through Resolve: CssTypeParser.Primitive and the
        // special cases recognise none of these names, so every match comes from a resolved definition.
        private static bool FunctionalType(TypeNode type) => !type.IsProperty && type.Name.EndsWith("()", StringComparison.Ordinal)
            && type.Name is not ("calc-size()" or "palette-mix()");

        private string[]? FunctionStartsFor(TypeNode type) =>
            FunctionStarts.GetOrAdd((type, _scope), static (key, matcher) => matcher.ResolvedFunctionNames(key.Node), this);

        private string[]? ResolvedFunctionNames(TypeNode type)
        {
            var names = new List<string>();
            foreach (GrammarNode reference in Resolve(type))
            {
                if (reference is not FunctionNode function) return null;
                names.Add(function.Name);
            }
            return names.ToArray();
        }

        private static bool StartsWith(string[] names, string name)
        {
            foreach (string candidate in names) if (NecronomiDom.Dom.Names.AsciiEqual(name, candidate)) return true;
            return false;
        }
    }
}
