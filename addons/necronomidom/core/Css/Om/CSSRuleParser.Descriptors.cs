using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;

namespace NecronomiDom.Css.Om
{
    internal static partial class CSSRuleParser
    {
        private static bool ValidProperty(CSSPropertyRule rule)
        {
            var syntaxValue = CssParser.ParseComponentValue(rule.Style.GetPropertyValue("syntax"));
            if (syntaxValue is not PreservedToken token || token.Token.Kind != CssTokenKind.String) return false;
            var inherits = rule.Style.GetPropertyValue("inherits");
            if (inherits != "true" && inherits != "false") return false;
            var syntax = token.Token.Value;
            if (!CssRegistrationSyntax.IsValid(syntax)) return false;
            var initial = rule.InitialValue;
            if (syntax.Trim() == "*") return true;
            if (initial.Length == 0) return false;
            var value = CssParser.ParseComponentValueList(initial);
            return Independent(value) && CssRegistrationSyntax.TryParseValue(syntax, value, out _);
        }
        private static bool Independent(IReadOnlyList<ComponentValue> values)
        {
            foreach (var value in values)
            {
                if (value is PreservedToken token)
                {
                    var t = token.Token;
                    if (t.Kind == CssTokenKind.Ident && (CssWideKeyword.IsWideKeyword(t.Value) || t.IsIdent("currentcolor"))) return false;
                    if (t.Kind == CssTokenKind.Dimension && Dom.Names.AsciiLower(t.Unit) is "em" or "rem" or "ex" or "rex" or "ch" or "rch" or "cap" or "rcap" or "ic" or "ric" or "lh" or "rlh" or
                        "vw" or "vh" or "vi" or "vb" or "vmin" or "vmax" or "svw" or "svh" or "svi" or "svb" or "svmin" or "svmax" or "lvw" or "lvh" or "lvi" or "lvb" or "lvmin" or "lvmax" or "dvw" or "dvh" or "dvi" or "dvb" or "dvmin" or "dvmax" or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax") return false;
                }
                else if (value is FunctionValue function)
                {
                    if (Dom.Names.AsciiLower(function.Name) is "var" or "env" or "attr" or "light-dark" or "anchor" or "anchor-size" || !Independent(function.Arguments)) return false;
                }
                else if (value is SimpleBlock block && !Independent(block.Value)) return false;
            }
            return true;
        }
        private static bool ValidCounter(CSSCounterStyleRule rule)
        {
            // Counter Styles 3 § 3: missing system-specific descriptors affect counter resolution,
            // not the syntactic existence of a CSSCounterStyleRule in CSSOM.
            return !CSSCounterStyleRule.NonOverridable(rule.Name);
        }
    }
}
