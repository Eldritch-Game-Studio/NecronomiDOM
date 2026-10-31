using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Colors;
using NecronomiDom.Css.Values.Images;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Values
{
    /// <summary>CSS Values 4 primitive and webref production parser; unknown types never match.</summary>
    public static class CssTypeParser
    {
        public static bool TryParse(string type, IReadOnlyList<ComponentValue> tokens, out CssValue? value)
        {
            if (type is not ("any-value" or "declaration-value") && !CssValueConstraints.Check("", tokens)) { value = null; return false; }
            GrammarMatch? result = new GrammarMatcher(tokens, "").Match(GrammarParser.Parse("<" + type + ">"));
            value = result == null ? null : CssSpecifiedValueNormalizer.Normalize("", result.Value);
            return result != null;
        }

        internal static bool Primitive(string type, ComponentValue component, double min, double max, out CssValue? value, CssNumericDimension? percentageContext = null)
        {
            value = null;
            if (type == "color") return CssColorParser.TryParse(component, out value);
            if (type == "hex-color") return component is PreservedToken { Token.Kind: CssTokenKind.Hash } && CssColorParser.TryParse(component, out value);
            if (type == "image") return CssImageParser.TryParse(component, out value);
            if (component is FunctionValue function)
            {
                CssNumericDimension? hint = type switch
                {
                    "number-percentage" or "opacity-value" => CssNumericDimension.Number,
                    "length-percentage" => CssNumericDimension.Length,
                    "number" or "integer" => percentageContext,
                    "percentage" => percentageContext ?? CssNumericDimension.Percentage,
                    _ => null,
                };
                if (CssMathParser.TryParse(function, hint, out CssValue? math) && math is CssMathValue numeric
                    && numeric.Fits(type == "opacity-value" ? "number-percentage" : type, hint))
                {
                    value = numeric.WithGrammarConstraints(type == "integer", min, max);
                    return true;
                }
                if (type == "url" && NecronomiDom.Dom.Names.AsciiEqual(function.Name, "url"))
                    return CssUrlParser.TryParse(function, out value);
                return false;
            }
            if (component is not PreservedToken token) return false;
            CssToken t = token.Token;
            switch (type)
            {
                case "ident": case "ident-token":
                    if (t.Kind == CssTokenKind.Ident) value = new CssKeywordValue(t.Value);
                    break;
                case "custom-ident":
                    if (t.Kind == CssTokenKind.Ident && !CssWideKeyword.IsWideKeyword(t.Value) && !t.IsIdent("default") && !t.IsIdent("revert-rule")) value = new CssKeywordValue(t.Value);
                    break;
                case "dashed-ident":
                    if (t.Kind == CssTokenKind.Ident && t.Value.StartsWith("--", StringComparison.Ordinal)) value = new CssKeywordValue(t.Value);
                    break;
                case "custom-property-name":
                    if (t.Kind == CssTokenKind.Ident && t.Value.StartsWith("--", StringComparison.Ordinal) && t.Value.Length > 2) value = new CssKeywordValue(t.Value);
                    break;
                case "string": case "string-token":
                    if (t.Kind == CssTokenKind.String) value = new CssStringValue(t.Value);
                    break;
                case "url": case "url-token":
                    if (t.Kind == CssTokenKind.Url) value = new CssUrlValue(t.Value);
                    break;
                case "number": case "number-token": case "integer": case "zero":
                    if (t.Kind == CssTokenKind.Number && (type != "integer" || t.NumberType == CssNumberType.Integer)
                        && (type != "zero" || t.Number == 0) && t.Number >= min && t.Number <= max)
                        value = type == "integer" ? new CssIntegerValue(CssNumericRange.IntegerLiteral(t.Number)) : new CssNumberValue(CssNumericRange.Literal(t.Number), t.NumberType == CssNumberType.Integer);
                    break;
                case "number-percentage": case "opacity-value":
                    if (t.Kind == CssTokenKind.Number && t.Number >= min && t.Number <= max) value = new CssNumberValue(CssNumericRange.Literal(t.Number), t.NumberType == CssNumberType.Integer);
                    else if (t.Kind == CssTokenKind.Percentage && t.Number / 100 >= min && t.Number / 100 <= max) value = new CssPercentageValue(CssNumericRange.Literal(t.Number));
                    break;
                case "percentage":
                    if (t.Kind == CssTokenKind.Percentage && t.Number >= min && t.Number <= max) value = new CssPercentageValue(CssNumericRange.Literal(t.Number));
                    break;
                case "length-percentage":
                    if (t.Kind == CssTokenKind.Percentage && t.Number >= min && t.Number <= max) value = new CssPercentageValue(CssNumericRange.Literal(t.Number));
                    else return Primitive("length", component, min, max, out value);
                    break;
                case "length": case "angle": case "time": case "frequency": case "resolution": case "flex":
                    if (t.Kind == CssTokenKind.Dimension && UnitType(t.Unit) == type && CssNumericRange.Canonical(t.Number, t.Unit) >= min && CssNumericRange.Canonical(t.Number, t.Unit) <= max)
                        value = new CssDimensionValue(CssNumericRange.Literal(t.Number), NecronomiDom.Dom.Names.AsciiEqual(t.Unit, "q") ? "Q" : NecronomiDom.Dom.Names.AsciiLower(t.Unit));
                    else if (type == "length" && t.Kind == CssTokenKind.Number && t.Number == 0 && min <= 0 && max >= 0)
                        value = new CssDimensionValue(0, "px");
                    break;
                case "dimension": case "dimension-token":
                    if (t.Kind == CssTokenKind.Dimension) value = new CssDimensionValue(CssNumericRange.Literal(t.Number), t.Unit);
                    break;
                case "hash-token":
                    if (t.Kind == CssTokenKind.Hash) value = new CssDelimValue("#" + t.Value);
                    break;
            }
            return value != null;
        }

        internal static string? UnitType(string unit) => NecronomiDom.Dom.Names.AsciiLower(unit) switch
        {
            "px" or "cm" or "mm" or "q" or "in" or "pt" or "pc" or "em" or "rem" or "ex" or "rex" or "ch" or "rch" or "cap" or "rcap" or "ic" or "ric" or "lh" or "rlh"
                or "vw" or "vh" or "vi" or "vb" or "vmin" or "vmax" or "svw" or "svh" or "svi" or "svb" or "svmin" or "svmax"
                or "lvw" or "lvh" or "lvi" or "lvb" or "lvmin" or "lvmax" or "dvw" or "dvh" or "dvi" or "dvb" or "dvmin" or "dvmax"
                or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax" => "length",
            "deg" or "grad" or "rad" or "turn" => "angle",
            "s" or "ms" => "time",
            "hz" or "khz" => "frequency",
            "dpi" or "dpcm" or "dppx" or "x" => "resolution",
            "fr" => "flex",
            _ => null,
        };
    }
}
