using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Values
{
    /// <summary>Grid shorthand serialization per CSS Grid 2 § 7 and CSSOM § 6.7.2.</summary>
    public static partial class ShorthandSerializer
    {
        private static string? Grid(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            string? rows = Child("grid-template-rows", get), columns = Child("grid-template-columns", get), areas = Child("grid-template-areas", get);
            if (rows == null || columns == null || areas == null) return null;
            if (definition.Name == "grid")
            {
                string? autoRows = Child("grid-auto-rows", get), autoColumns = Child("grid-auto-columns", get), flow = Child("grid-auto-flow", get);
                if (autoRows == null || autoColumns == null || flow == null) return null;
                if (autoRows != "auto" || autoColumns != "auto" || flow != "row")
                {
                    if (areas != "none") return null;
                    bool dense = flow.Contains("dense", StringComparison.Ordinal);
                    if (flow.Contains("column", StringComparison.Ordinal))
                    {
                        if (columns != "none" || autoRows != "auto") return null;
                        return rows + " / auto-flow" + (dense ? " dense" : "") + (autoColumns == "auto" ? "" : " " + autoColumns);
                    }
                    if (rows != "none" || autoColumns != "auto") return null;
                    return "auto-flow" + (dense ? " dense" : "") + (autoRows == "auto" ? "" : " " + autoRows) + " / " + columns;
                }
            }
            if (areas == "none") return rows == "none" && columns == "none" ? "none" : rows + " / " + columns;
            IReadOnlyList<ComponentValue> rowTokens = TokenText.WithoutWhitespace(CssParser.ParseComponentValueList(rows));
            IReadOnlyList<ComponentValue> areaTokens = TokenText.WithoutWhitespace(CssParser.ParseComponentValueList(areas));
            var text = new List<string>();
            int track = 0;
            foreach (ComponentValue area in areaTokens)
            {
                if (area is not PreservedToken { Token.Kind: CssTokenKind.String } s) return null;
                while (track < rowTokens.Count && rowTokens[track] is SimpleBlock) text.Add(CssTokenSerializer.Serialize(rowTokens[track++]));
                text.Add(CssSerialization.SerializeString(s.Token.Value));
                if (track >= rowTokens.Count || rowTokens[track] is SimpleBlock) return null;
                string size = CssTokenSerializer.Serialize(rowTokens[track++]);
                if (size != "auto") text.Add(size);
            }
            while (track < rowTokens.Count && rowTokens[track] is SimpleBlock) text.Add(CssTokenSerializer.Serialize(rowTokens[track++]));
            if (track != rowTokens.Count) return null;
            string result = string.Join(" ", text);
            return columns == "none" ? result : result + " / " + columns;
        }

        private static string? GridLines(PropertyDefinition definition, Func<string, CssValue?> get)
        {
            var expected = new Dictionary<string, CssValue>(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string name in definition.Longhands)
            {
                CssValue? value = get(name);
                if (value == null) return null;
                expected[name] = value;
                parts.Add(value.Serialize());
            }
            string text = string.Join(" / ", parts);
            while (parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);
                string shorter = string.Join(" / ", parts);
                if (!Equivalent(definition.Name, shorter, expected)) break;
                text = shorter;
            }
            return text;
        }
    }
}
