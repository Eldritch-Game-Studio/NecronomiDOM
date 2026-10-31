using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values.Grammar;

namespace NecronomiDom.Css.Values
{
    internal static class CssSpecifiedValueNormalizer
    {
        internal static CssValue Normalize(string property, CssValue value)
        {
            value = CssLayoutSpecifiedValues.Normalize(property, value);
            value = CssFontSpecifiedValues.Normalize(property, value);
            value = CssTransformSpecifiedValues.Normalize(property, value);
            value = CssTimelineRangeValues.Normalize(property, value);
            if (property == "opacity" && value is CssPercentageValue percentage)
                return new CssNumberValue(percentage.Value / 100, false);
            if (value is CssListValue { Separator: CssListSeparator.Comma } comma)
            {
                var items = new List<CssValue>(comma.Items.Count);
                foreach (CssValue item in comma.Items) items.Add(Normalize(property, item));
                return new CssListValue(items, CssListSeparator.Comma);
            }
            if (property is "object-position" or "mask-position")
                value = CssTransformSpecifiedValues.Origin(value);
            if (property is "background-size" or "mask-size" && value is not (CssListValue or CssKeywordValue))
                return new CssListValue(new[] { value, new CssKeywordValue("auto") }, CssListSeparator.Space);
            if (property == "font-family" && value is CssStringValue family)
            {
                IReadOnlyList<ComponentValue> tokens = CssParser.ParseComponentValueList(family.Value);
                bool identifiers = !family.Value.Contains('\\');
                foreach (ComponentValue token in tokens)
                    identifiers &= token is PreservedToken p && p.Token.Kind is CssTokenKind.Ident or CssTokenKind.Whitespace;
                GrammarMatch? unquoted = identifiers ? new GrammarMatcher(tokens, "font-family").Match(GrammarParser.Parse("<font-family-name>")) : null;
                if (unquoted != null) return unquoted.Value;
            }
            if (property == "font-feature-settings" && value is CssListValue { Separator: CssListSeparator.Space } feature
                && feature.Items.Count == 2)
            {
                CssValue setting = feature.Items[1];
                if (setting is CssNumberValue { Value: 1 } || setting is CssKeywordValue on && on.Is("on")) return feature.Items[0];
                if (setting is CssKeywordValue off && off.Is("off")) return new CssListValue(new[] { feature.Items[0], new CssNumberValue(0, true) }, CssListSeparator.Space);
            }
            if (value is CssListValue { Separator: CssListSeparator.Space } list)
            {
                if ((property is "background-repeat" or "mask-repeat" or "border-image-repeat" or "mask-border-repeat" or "border-spacing"
                        || property.StartsWith("border-", StringComparison.Ordinal) && property.EndsWith("-radius", StringComparison.Ordinal))
                    && list.Items.Count == 2 && list.Items[0].Serialize() == list.Items[1].Serialize()) return list.Items[0];
                if (property is "background-size" or "mask-size" && list.Items.Count == 2 && list.Items[0] is CssKeywordValue firstAuto
                    && firstAuto.Is("auto") && list.Items[1] is CssKeywordValue secondAuto && secondAuto.Is("auto")) return list.Items[0];
                var items = new List<CssValue>(list.Items.Count);
                foreach (CssValue item in list.Items) items.Add(Normalize("", item));
                return new CssListValue(items, list.Separator);
            }
            if (value is CssFunctionValue function)
            {
                CssValue? args = function.Arguments;
                string name = NecronomiDom.Dom.Names.AsciiLower(function.Name);
                if (args != null) args = CssTransformSpecifiedValues.FunctionArguments(name, args);
                args = CssShapeSpecifiedValues.FunctionArguments(name, args);
                if (name == "steps" && args is CssListValue { Separator: CssListSeparator.Comma } steps && steps.Items.Count == 2
                    && steps.Items[1] is CssKeywordValue end && (end.Is("end") || end.Is("jump-end"))) args = steps.Items[0];
                if (name is "rotate" or "rotatex" or "rotatey" or "rotatez" or "skewx" or "skewy" or "hue-rotate"
                    && args is CssNumberValue { Value: 0 }) args = new CssDimensionValue(0, "deg");
                // These Filter Effects functions clamp their upper bound at specified-value time.
                if (name is "grayscale" or "sepia" or "invert" or "opacity")
                {
                    if (args is CssPercentageValue { Value: > 100 }) args = new CssPercentageValue(100);
                    else if (args is CssNumberValue { Value: > 1 } scalar) args = new CssNumberValue(1, scalar.IsInteger);
                }
                return CssLinearEasingValues.Specified(new CssFunctionValue(CssValueParser.CanonicalFunction(function.Name), args));
            }
            return value;
        }
    }
}
