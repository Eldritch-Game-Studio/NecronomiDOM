using System;
using System.Collections.Generic;
using System.Text;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Selectors
{
    public static partial class SelectorParser
    {
        private static readonly HashSet<string> Classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "first-child", "last-child", "only-child", "first-of-type", "last-of-type", "only-of-type", "empty", "root", "scope",
            "hover", "active", "focus", "focus-visible", "focus-within", "enabled", "disabled", "checked", "indeterminate",
            "placeholder-shown", "default", "valid", "invalid", "in-range", "out-of-range", "read-only", "read-write",
            "defined", "modal", "popover-open", "host", "link", "visited", "any-link", "target", "target-within",
            "required", "optional", "user-valid", "user-invalid", "autofill", "open", "window-inactive",
        };
        private static readonly HashSet<string> Elements = new HashSet<string>(StringComparer.Ordinal)
        {
            "before", "after", "marker", "placeholder", "selection", "backdrop", "first-line", "first-letter", "picker-icon", "checkmark",
            "file-selector-button",
            "-webkit-slider-thumb", "-webkit-slider-runnable-track",
        };
        private sealed partial class SelectorReader
        {
            private SimpleSelector? Pseudo(PseudoElementSelector? previous = null)
            {
                _p++;
                var element = Kind(CssTokenKind.Colon);
                if (element) _p++;
                var name = Ident();
                FunctionValue? function = null;
                if (name == null && !AtEnd && _values[_p] is FunctionValue f) { function = f; name = f.Name; _p++; }
                if (name == null) return null;
                name = Dom.Names.AsciiLower(name);
                if (!element && name is "before" or "after" or "first-line" or "first-letter") element = true;
                if (element)
                {
                    if (!_options.AllowPseudoElements) return null;
                    if (function == null) return Elements.Contains(name) ? new PseudoElementSelector(name) : null;
                    var args = CssParser.Trim(function.Arguments);
                    if (name == "slotted")
                    {
                        var list = ParseSelectorList(args, _options with { Forgiving = false, Relative = false, AllowPseudoElements = false, IgnoreSubjectDefaultNamespace = false });
                        return IsSingleCompound(list) ? new PseudoElementSelector(name, list) : null;
                    }
                    if (name == "picker") return args.Count == 1 && CssParser.IsIdent(args[0], "select") ? new PseudoElementSelector(name, Argument: "select") : null;
                    if (name == "part")
                    {
                        var text = IdentifierSequence(args);
                        return text == null ? null : new PseudoElementSelector(name, Argument: text);
                    }
                    return null;
                }
                if (function == null) return Classes.Contains(name) ? new PseudoClassSelector(name) : null;
                var arguments = CssParser.Trim(function.Arguments);
                if (name is "is" or "where" or "not" or "has" or "host" or "host-context")
                {
                    if (name == "has" && _options.InsideHas) return null;
                    var list = ParseSelectorList(arguments, _options with
                    {
                        Forgiving = name is "is" or "where", Relative = name == "has", AllowPseudoElements = false,
                        InsideHas = _options.InsideHas || name == "has",
                        IgnoreSubjectDefaultNamespace = name is "is" or "where" or "not",
                        CompoundOnly = name != "has" && (_options.CompoundOnly || name is "host" or "host-context"),
                        PartArgument = name != "has" && (_options.PartArgument || previous?.Name == "part"),
                    });
                    if (list == null || (name is "host" or "host-context" && !IsSingleCompound(list))) return null;
                    return new PseudoClassSelector(name, list);
                }
                if (name is "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type")
                {
                    var nth = ParseNth(arguments, name is "nth-child" or "nth-last-child", _options);
                    return nth == null ? null : new PseudoClassSelector(name, Nth: nth);
                }
                if (name == "state")
                    return arguments.Count == 1 && arguments[0] is PreservedToken { Token.Kind: CssTokenKind.Ident } state ? new PseudoClassSelector(name, Argument: CssSerialization.SerializeIdentifier(state.Token.Value)) { DecodedArgument = state.Token.Value } : null;
                if (name == "dir")
                    return arguments.Count == 1 && (CssParser.IsIdent(arguments[0], "ltr") || CssParser.IsIdent(arguments[0], "rtl")) ? new PseudoClassSelector(name, Argument: Dom.Names.AsciiLower(((PreservedToken)arguments[0]).Token.Value)) : null;
                if (name == "lang")
                {
                    var result = new StringBuilder();
                    var ranges = new List<string>();
                    foreach (var entry in CssParser.SplitComma(arguments))
                    {
                        var trimmed = CssParser.Trim(entry);
                        if (trimmed.Count != 1 || trimmed[0] is not PreservedToken token || token.Token.Kind is not (CssTokenKind.Ident or CssTokenKind.String)) return null;
                        if (result.Length > 0) result.Append(", ");
                        result.Append(CssTokenSerializer.Serialize(token.Token));
                        ranges.Add(token.Token.Value);
                    }
                    return result.Length > 0 ? new PseudoClassSelector(name, Argument: result.ToString()) { LanguageRanges = ranges } : null;
                }
                return null;
            }
            private static bool IsSingleCompound(SelectorList? list) => list != null && list.Selectors.Count == 1 && list.Selectors[0].Compounds.Count == 1 && !list.Selectors[0].LeadingCombinator.HasValue;
            private static string? IdentifierSequence(IReadOnlyList<ComponentValue> args)
            {
                var result = new StringBuilder();
                foreach (var value in args)
                {
                    if (CssParser.Is(value, CssTokenKind.Whitespace)) continue;
                    if (value is not PreservedToken token || token.Token.Kind != CssTokenKind.Ident) return null;
                    if (result.Length > 0) result.Append(' ');
                    result.Append(CssSerialization.SerializeIdentifier(token.Token.Value));
                }
                return result.Length > 0 ? result.ToString() : null;
            }
        }
    }
}
